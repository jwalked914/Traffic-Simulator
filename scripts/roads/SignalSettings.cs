public enum SignalType { None, AllWayStop, TwoWayStop, TrafficLight }

// signal settings for one intersection cell
// MajorIsVertical: which road has priority (two-way stops, first green for lights)
// false = the horizontal road is major, so default settings need no extra value
public readonly record struct SignalSettings(SignalType Type, bool MajorIsVertical = false);
