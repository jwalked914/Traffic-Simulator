using Godot;
using System;
using System.Collections.Generic;
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
		
		var paths = _roads.Segments.Select(s => CanonicalOrder.CanonicalPath(s.Path)).ToList();
		paths.Sort(CanonicalOrder.ComparePaths);
		foreach (var path in paths)
			data.Roads.Add(new RoadData { Path = path.Select(c => new[] {c.X, c.Y}).ToList() });
			
		foreach (Structure s in _structures.Structures
			.OrderBy(s => s.AnchorCell, Comparer<Vector2I>.Create(CanonicalOrder.CompareCells)))
		{
			data.Structures.Add(new StructureData
			{
				Anchor = new[] {s.AnchorCell.X, s.AnchorCell.Y},
				Type = s.Type,
				Size = new[] {s.SizeInCells.X, s.SizeInCells.Y},
				ParkingRows = s.ParkingRows
			});
		}
		return data;
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
			if (!TryCell(s.Anchor, offset, out Vector2I anchor) || !TryCell(s.Size, Vector2I.Zero, out Vector2I size))
			{
				result.Errors.Add($"Structure {i}: bad anchor or size.");
				continue;
			}
			
			try 
			{
				_ = new Structure(anchor, s.Type, size, s.ParkingRows, _roads.CellSize, _roads.RoadWidth);
			} catch (System.ArgumentException e)
			{
				result.Errors.Add($"Structure {i}: {e.Message}");
				continue;
			}
			
			var rect = new Rect2I(anchor, size);
			if (bounds.Any(b => b.Intersects(rect))) result.Errors.Add($"Structure {i}: overlaps another imported building.");
			if (roadCells.Any(c => rect.HasPoint(c))) result.Errors.Add($"Structure {i}: overlaps an imported road.");
			bounds.Add(rect);
			
			if (checkLiveWorld && !_structures.CanPlace(anchor, s.Type, size, s.ParkingRows, out string err))
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
				if (!_roads.TryAddRoad(path, out _)) result.Errors.Add("A road was rejected.");
			}
			foreach (StructureData s in data.Structures)
			{
				var anchor = new Vector2I(s.Anchor[0], s.Anchor[1]) + offset;
				var size = new Vector2I (s.Size[0], s.Size[1]);
				if (!_structures.TryPlace(anchor, s.Type, size, s.ParkingRows, out _, out string e))
					result.Errors.Add(e);
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
