using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class ScenarioSerializer
{
	public const int CurrentVersion = 1;

	private static readonly JsonSerializerOptions Options = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public static string ToJson(ScenarioData data) => JsonSerializer.Serialize(data, Options);

	public static bool TryFromJson(string json, out ScenarioData data, out string error)
	{
		data = null;
		error = "";
		try
		{
			data = JsonSerializer.Deserialize<ScenarioData>(json, Options);
		}
		catch (Exception e)
		{
			error = $"Could not parse file: {e.Message}";
			return false;
		}

		if (data == null) { error = "File is empty."; return false; }
		if (data.Version != CurrentVersion)
		{
			error = $"Unsupported file version {data.Version} (expected {CurrentVersion}).";
			return false;
		}
		return true;
	}

	public static bool TryWriteFile(string path, ScenarioData data, out string error)
	{
		error = "";
		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			error = $"Could not write {path}: {FileAccess.GetOpenError()}";
			return false;
		}
		file.StoreString(ToJson(data));
		return true;
	}

	public static bool TryReadFile(string path, out ScenarioData data, out string error)
	{
		data = null;
		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			error = $"Could not read {path}: {FileAccess.GetOpenError()}";
			return false;
		}
		return TryFromJson(file.GetAsText(), out data, out error);
	}
}
