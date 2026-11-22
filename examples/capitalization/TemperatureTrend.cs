[McpMethod("Reads the temperatures stored by RecordTemperature and reports their minimum, maximum, average, and the change between the first and the last")]
public string TemperatureTrend()
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    const string prefix = "temperature:";

    // ListAllData and GetData are base methods: a dynamic method can call them directly.
    using var index = System.Text.Json.JsonDocument.Parse(ListAllData());
    var keys = index.RootElement.EnumerateArray()
        .Select(entry => entry.GetProperty("key").GetString())
        .Where(key => key != null && key.StartsWith(prefix))
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToList();

    if (keys.Count == 0)
        return "No temperature recorded yet. Call RecordTemperature first.";

    var values = new List<double>();
    foreach (var key in keys)
    {
        using var stored = System.Text.Json.JsonDocument.Parse(GetData(key!));
        values.Add(double.Parse(stored.RootElement.GetProperty("value").GetString()!, inv));
    }

    return keys.Count + " readings from " + keys.First()!.Substring(prefix.Length) + " to " + keys.Last()!.Substring(prefix.Length)
        + ": min " + values.Min().ToString(inv)
        + ", max " + values.Max().ToString(inv)
        + ", average " + values.Average().ToString("0.0", inv)
        + ", change " + (values.Last() - values.First()).ToString("+0.0;-0.0;0.0", inv) + " C.";
}
