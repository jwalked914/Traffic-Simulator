using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// Marks a public property as part of a structure's saved data.
[AttributeUsage(AttributeTargets.Property)]
public class PersistAttribute : Attribute { }

// Copies [Persist] properties to and from a string -> JSON property bag.
public static class PropSerializer
{
	private static IEnumerable<PropertyInfo> Persisted(Type type) =>
		type.GetProperties().Where(p => p.GetCustomAttribute<PersistAttribute>() != null);

	public static Dictionary<string, JsonElement> Write(object obj)
	{
		var bag = new Dictionary<string, JsonElement>();
		foreach (PropertyInfo p in Persisted(obj.GetType()))
		{
			bag[p.Name] = JsonSerializer.SerializeToElement(
				p.GetValue(obj), p.PropertyType, ScenarioSerializer.Options);
		}
		return bag;
	}

	// Only keys present in the bag are applied, so missing keys keep their defaults.
	// Unknown keys are ignored. Returns a list of properties that failed to parse.
	public static List<string> Read(object obj, Dictionary<string, JsonElement> bag)
	{
		var errors = new List<string>();
		foreach (PropertyInfo p in Persisted(obj.GetType()))
		{
			if (!bag.TryGetValue(p.Name, out JsonElement element)) continue;

			try
			{
				p.SetValue(obj, element.Deserialize(p.PropertyType, ScenarioSerializer.Options));
			}
			catch (Exception e) when (e is JsonException || e is InvalidOperationException)
			{
				errors.Add($"{p.Name}: {e.Message}");
			}
		}
		return errors;
	}
}

public class Vector2IConverter : JsonConverter<Vector2I>
{
	public override Vector2I Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		int[] values = JsonSerializer.Deserialize<int[]>(ref reader, options);
		if (values == null || values.Length != 2)
			throw new JsonException("Vector2I must be an array of two integers.");
		return new Vector2I(values[0], values[1]);
	}

	public override void Write(Utf8JsonWriter writer, Vector2I value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteNumberValue(value.X);
		writer.WriteNumberValue(value.Y);
		writer.WriteEndArray();
	}
}
