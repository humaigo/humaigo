[McpMethod("Returns the current temperature (C), wind speed (km/h) and observation time for the given coordinates, using the free Open-Meteo API (no key needed)")]
public string GetCurrentWeather(
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

    return weather.GetProperty("temperature").GetDouble().ToString(inv) + " C, wind "
        + weather.GetProperty("windspeed").GetDouble().ToString(inv) + " km/h, observed at "
        + weather.GetProperty("time").GetString();
}
