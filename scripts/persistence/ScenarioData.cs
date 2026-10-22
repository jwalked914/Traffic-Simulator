using Godot;
using System.Collections.Generic;

public class ScenarioData
{
	public int Version {get; set;} = 1;
	public float CellSize {get; set;}
	public float RoadWidth {get; set;}
	public List<RoadData> Roads {get; set;} = new();
	public List <StructureData> Structures {get; set;} = new();
}

public class RoadData
{
	public List<int[]> Path {get; set;} = new();
}

public class StructureData
{
	public int[] Anchor {get; set;}
	public StructureType Type {get; set;}
	public int[] Size {get; set;}
	public int ParkingRows {get; set;}
}

public class ImportResult
{
	public List<string> Errors {get;} = new();
	public bool Success => Errors.Count == 0;
}
