using System.Text.Json;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>
/// The examples in the examples/ folder are real tools, so they are compiled and run here. This keeps them from
/// going out of date. The ones that call the Internet are only compiled, never called.
/// </summary>
public class ExamplesTests
{
    private static string Example(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "HumAiGo.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "examples", folder, file));
    }

    private static void Add(TempRegistry t, string folder, string file, params string[] dependencies)
    {
        var result = dependencies.Length == 0
            ? t.Call("AddMethod", ("methodCode", Example(folder, file)))
            : t.Call("AddMethod", ("methodCode", Example(folder, file)), ("dependencies", JsonSerializer.SerializeToElement(dependencies)));

        Assert.StartsWith("Successfully added", result);
    }

    [Fact]
    public void The_weather_example_compiles_and_describes_its_parameters()
    {
        using var t = new TempRegistry();

        Add(t, "weather-open-meteo", "GetCurrentWeather.cs");

        var tool = t.Registry.GetToolsForMcp()
            .Select(candidate => JsonSerializer.SerializeToElement(candidate))
            .Single(candidate => candidate.GetProperty("name").GetString() == "GetCurrentWeather");
        var latitude = tool.GetProperty("inputSchema").GetProperty("properties").GetProperty("latitude");
        Assert.Equal("number", latitude.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(latitude.GetProperty("description").GetString()));
    }

    [Fact]
    public void The_dependencies_example_combines_two_tools_by_copy()
    {
        using var t = new TempRegistry();

        Add(t, "dependencies", "Add.cs");
        Add(t, "dependencies", "Multiply.cs");
        Add(t, "dependencies", "AddThenMultiply.cs", "Add", "Multiply");

        Assert.Equal("84", t.Call("AddThenMultiply", ("a", 3), ("b", 4)));
        Assert.Contains("up to date", t.Call("CheckOutdatedDependencies"));
    }

    [Fact]
    public void The_capitalization_example_turns_stored_readings_into_a_trend()
    {
        using var t = new TempRegistry();
        Add(t, "capitalization", "RecordTemperature.cs");
        Add(t, "capitalization", "TemperatureTrend.cs");

        Assert.Contains("No temperature recorded yet", t.Call("TemperatureTrend"));

        // RecordTemperature would write these from the Internet; here they are written by hand.
        t.Call("SetData", ("key", "temperature:2026-01-01T10:00"), ("value", "10.0"));
        t.Call("SetData", ("key", "temperature:2026-01-01T11:00"), ("value", "12.5"));
        t.Call("SetData", ("key", "temperature:2026-01-01T12:00"), ("value", "11.0"));
        t.Call("SetData", ("key", "unrelated"), ("value", "not a temperature"));

        var trend = t.Call("TemperatureTrend");

        Assert.Contains("3 readings from 2026-01-01T10:00 to 2026-01-01T12:00", trend);
        Assert.Contains("min 10", trend);
        Assert.Contains("max 12.5", trend);
        Assert.Contains("average 11.2", trend);
        Assert.Contains("change +1.0", trend);
    }

    [Fact]
    public void Common_namespaces_are_available_without_using_directives()
    {
        using var t = new TempRegistry();

        var result = t.Call("AddMethod", ("methodCode", """
            [McpMethod("Sums the even numbers of a list")]
            public int SumEven(string numbers) => numbers.Split(',').Select(int.Parse).Where(n => n % 2 == 0).Sum();
            """));

        Assert.StartsWith("Successfully added SumEven", result);
        Assert.Equal("6", t.Call("SumEven", ("numbers", "1,2,3,4,5")));
    }
}
