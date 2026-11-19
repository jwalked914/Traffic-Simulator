using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class ScenarioManager : Node
{
	private RoadGraph _roads;
	private StructureGraph _structures;
	private LaneGraph _lanes;

	public string CurrentPath {get; private set;}
	public bool IsDirty {get; private set;}
	public event Action DirtyChanged;

	public override void _Ready()
	{
		_roads = GetNode<RoadGraph>("../RoadGraph");
		_structures = GetNode<StructureGraph>("../StructureGraph");
		_lanes = GetNode<LaneGraph>("../LaneGraph");

		_roads.RoadsChanged += MarkDirty;
		_structures.StructuresChanged += MarkDirty;
	}

	public override void _ExitTree()
	{
		if (_roads != null) _roads.RoadsChanged -= MarkDirty;
		if (_structures != null) _structures.StructuresChanged -= MarkDirty;
	}

	public void NewScenario()
	{
		_structures.Clear();
		_roads.ClearRoads();
		_lanes.Clear();

		CurrentPath = null;
		SetDirty(false);
	}

	public ScenarioData Capture()
	{
		var data = new ScenarioData
		{
			CellSize = _roads.CellSize,
			RoadWidth = _roads.RoadWidth
		};

		var roads = _roads.Segments.Select(s => (
			Path: CanonicalOrder.CanonicalPath(s.Path),
			SpeedLimit: s.SpeedLimit
		))
		.ToList();

		roads.Sort((a, b) => CanonicalOrder.ComparePaths(a.Path, b.Path));

		foreach (var road in roads)
		{
			data.Roads.Add(new RoadData
			{
				Path = road.Path.Select(c => new[] { c.X, c.Y }).ToList(),
				SpeedLimit = road.SpeedLimit
			});
		}
		foreach (Structure s in _structures.Structures
			.OrderBy(s => s.AnchorCell, Comparer<Vector2I>.Create(CanonicalOrder.CompareCells)))
		{
			data.Structures.Add(new StructureData
			{
				Anchor = s.AnchorCell,
				Type = s.Type,
				Props = PropSerializer.Write(s)
			});
		}
		return data;
	}

	// copies the current selection into a fragment with cells relative to its top-left
	public ScenarioData CaptureSelection()
	{
		if (!_roads.TryGetSelection(out Vector2I origin, out _)) return null;

		var data = new ScenarioData { CellSize = _roads.CellSize, RoadWidth = _roads.RoadWidth };
		var selected = _roads.SelectedRoadCells;

		void Flush(List<Vector2I> run, float speed)
		{
			if (run.Count < 2) return; // a lone cell isn't a road
			data.Roads.Add(new RoadData
			{
				Path = run.Select(c => new[] { c.X - origin.X, c.Y - origin.Y }).ToList(),
				SpeedLimit = speed
			});
		}

		foreach (RoadSegment segment in _roads.Segments)
		{
			var run = new List<Vector2I>();
			foreach (Vector2I cell in segment.Path)
			{
				if (selected.Contains(cell)) { run.Add(cell); continue; }
				Flush(run, segment.SpeedLimit);
				run = new List<Vector2I>();
			}
			Flush(run, segment.SpeedLimit);
		}

		foreach (Structure s in _roads.SelectedStructures)
		{
			data.Structures.Add(new StructureData
			{
				Anchor = s.AnchorCell - origin,
				Type = s.Type,
				Props = PropSerializer.Write(s)
			});
		}

		return data.Roads.Count + data.Structures.Count == 0 ? null : data;
	}

	// deletes everything in the current selection; returns false if nothing was selected
	public bool DeleteSelection(out string error)
	{
		error = "";
		if (!_roads.HasSelection) return false;

		// snapshot first: deleting fires change events that clear the live selection
		var cells = new HashSet<Vector2I>(_roads.SelectedRoadCells);
		var structures = _roads.SelectedStructures.ToList();

		_structures.BeginBatch();
		_roads.BeginBatch();
		try
		{
			foreach (Structure s in structures)
				if (!_structures.TryRemove(s, out string e)) error = e;

			_roads.DeleteRoadCellsWhere(cells.Contains);
		}
		finally
		{
			_roads.EndBatch();
			_structures.EndBatch();
		}

		_roads.ClearSelection();
		return true;
	}

	public ImportResult Paste(ScenarioData data, Vector2I offset, bool overwrite)
	{
		// overwrite clears the footprint itself, so only check the fragment on its own
		ImportResult check = Validate(data, offset, checkLiveWorld: !overwrite);
		if (!check.Success) return check;

		if (overwrite) ClearFootprint(data, offset);
		return Commit(data, offset);
	}

	// removes roads and whole buildings under the fragment's cells
	private void ClearFootprint(ScenarioData data, Vector2I offset)
	{
		var cells = new HashSet<Vector2I>();
		foreach (RoadData r in data.Roads)
			foreach (int[] a in r.Path)
				cells.Add(new Vector2I(a[0], a[1]) + offset);

		foreach (StructureData s in data.Structures)
		{
			var st = new Structure(s.Anchor + offset, s.Type, s.Props, _roads.CellSize, _roads.RoadWidth);
			Rect2I b = st.Bounds;
			for (int x = b.Position.X; x < b.End.X; x++)
				for (int y = b.Position.Y; y < b.End.Y; y++)
					cells.Add(new Vector2I(x, y));
		}

		_structures.BeginBatch();
		_roads.BeginBatch();
		try
		{
			var doomed = new HashSet<Structure>();
			foreach (Vector2I c in cells)
			{
				Structure existing = _structures.GetAt(c);
				if (existing != null) doomed.Add(existing);
			}
			foreach (Structure s in doomed) _structures.TryRemove(s, out _);

			_roads.DeleteRoadCellsWhere(cells.Contains);
		}
		finally
		{
			_roads.EndBatch();
			_structures.EndBatch();
		}
	}

	private static bool TryCell(int[] a, Vector2I offset, out Vector2I cell)
	{
		cell = default;
		if (a == null || a.Length != 2) return false;
		cell = new Vector2I(a[0], a[1]) + offset;
		return true;
	}

	public ImportResult Validate(ScenarioData data, Vector2I offset, bool checkLiveWorld)
	{
		var result = new ImportResult();
		if (!Mathf.IsEqualApprox(data.CellSize, _roads.CellSize) ||
			!Mathf.IsEqualApprox(data.RoadWidth, _roads.RoadWidth))
		{
			result.Errors.Add("File was saved with a different road scale.");
			return result;
		}

		var roadCells = new HashSet<Vector2I>();
		for (int r = 0; r < data.Roads.Count; r++)
		{
			var path = new List<Vector2I>();
			foreach (int[] raw in data.Roads[r].Path)
			{
				if (!TryCell(raw, offset, out Vector2I cell))
				{
					result.Errors.Add($"Road {r}: bad cell.");
					break;
				}
				path.Add(cell);
			}
			if (path.Count != data.Roads[r].Path.Count) continue;

			if (checkLiveWorld) { if (!_roads.CanAddRoad(path, out string e)) result.Errors.Add($"Road {r}: {e}"); }
			else if (path.Count < 2 || path.Zip(path.Skip(1), (a, b) => Mathf.Abs(b.X - a.X) + Mathf.Abs(b.Y - a.Y)).Any(d => d != 1))
				result.Errors.Add($"Road {r}: cells must form a connected path of at least two.");

			foreach (Vector2I c in path) roadCells.Add(c);
		}

		var bounds = new List<Rect2I>();
		for (int i = 0; i < data.Structures.Count; ++i)
		{
			StructureData s = data.Structures[i];
			Vector2I anchor = s.Anchor + offset;

			Structure structure;
			try
			{
				structure = new Structure(anchor, s.Type, s.Props, _roads.CellSize, _roads.RoadWidth);
			} catch (ArgumentException e)
			{
				result.Errors.Add($"Structure {i}: {e.Message}");
				continue;
			}

			Rect2I rect = structure.Bounds;
			if (bounds.Any(b => b.Intersects(rect))) result.Errors.Add($"Structure {i}: overlaps another imported building.");
			if (roadCells.Any(c => rect.HasPoint(c))) result.Errors.Add($"Structure {i}: overlaps an imported road.");
			bounds.Add(rect);

			if (checkLiveWorld && !_structures.CanPlace(structure, out string err))
				result.Errors.Add($"Structure {i}: {err}");
		}
		return result;
	}

	private ImportResult Commit(ScenarioData data, Vector2I offset)
	{
		var result = new ImportResult();
		_structures.BeginBatch();
		_roads.BeginBatch();
		try
		{
			foreach (RoadData road in data.Roads)
			{
				var path = road.Path.Select(a => new Vector2I(a[0], a[1]) + offset).ToList();
				if (!_roads.TryAddRoad(path, road.SpeedLimit, out _)) result.Errors.Add("A road was rejected.");
			}
			foreach (StructureData s in data.Structures)
			{
				Vector2I anchor = s.Anchor + offset;

				Structure structure;
				try
				{
					structure = new Structure(anchor, s.Type, s.Props, _roads.CellSize, _roads.RoadWidth);
				}
				catch (ArgumentException e)
				{
					result.Errors.Add(e.Message);
					continue;
				}

				if (!_structures.TryPlace(structure, out _, out string error))
					result.Errors.Add(error);
			}
		}
		finally
		{
			_roads.EndBatch();
			_structures.EndBatch();
		}
		_structures.Select(null);
		return result;
	}

	public ImportResult SaveTo(string path)
	{
		var result = new ImportResult();
		if (!ScenarioSerializer.TryWriteFile(path, Capture(), out string error))
		{
			result.Errors.Add(error);
			return result;
		}
		CurrentPath = path;
		SetDirty(false);
		return result;
	}

	public ImportResult LoadFrom(string path, bool replace = true)
	{
		var failed = new ImportResult();
		if (!ScenarioSerializer.TryReadFile(path, out ScenarioData data, out string error))
		{
			failed.Errors.Add(error);
			return failed;
		}

		ImportResult validation = Validate(data, Vector2I.Zero, checkLiveWorld: !replace);
		if (!validation.Success) return validation;

		if (replace) NewScenario();
		ImportResult result = Commit(data, Vector2I.Zero);

		if (replace)
		{
			CurrentPath = path;
			SetDirty(false);
		}
		return result;
	}

	private void MarkDirty() => SetDirty(true);
	private void SetDirty(bool value)
	{
		if (IsDirty == value) return;
		IsDirty = value;
		DirtyChanged?.Invoke();
	}
}
