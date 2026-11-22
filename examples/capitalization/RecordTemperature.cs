[McpMethod("Reads the current temperature at the given coordinates from Open-Meteo and stores it, so that TemperatureTrend can use it later")]
public string RecordTemperature(
    [System.ComponentModel.Description("Latitude in decimal degrees")] double latitude,
    [System.ComponentModel.Description("Longitude in decimal degrees")] double longitude)
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    string url = "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(inv)
        + "&longitude=" + longitude.ToString(inv) + "&current_weather=true";

    string json;
    using (var client = new System.Net.Http.HttpClient())
        json = client.GetStringAsync(url).GetAwaiter().GetResult();

    using var document = System.Text.Json.JsonDocument.Parse(json);
    var weather = document.RootElement.GetProperty("current_weather");
    var temperature = weather.GetProperty("temperature").GetDouble().ToString(inv);
    var time = weather.GetProperty("time").GetString();

    // SetData is a base method: a dynamic method can call it directly.
    return SetData("temperature:" + time, temperature,
        "Open-Meteo reading at " + latitude.ToString(inv) + "," + longitude.ToString(inv));
}
