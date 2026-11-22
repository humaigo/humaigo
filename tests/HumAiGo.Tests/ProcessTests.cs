using System.Text.Json;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>Tests that start the real server executable and talk to it over standard input and output.</summary>
public class ProcessTests : IDisposable
{
    private readonly List<string> _registries = new();

    private string NewRegistry()
    {
        var id = TempRegistry.NewId();
        _registries.Add(id);
        return id;
    }

    public void Dispose()
    {
        foreach (var id in _registries) TempRegistry.DeleteFolder(id);
    }

    private static object Initialize(string protocolVersion) =>
        new { protocolVersion, capabilities = new { }, clientInfo = new { name = "test", version = "1" } };

    [Fact]
    public async Task Handshake_ping_resources_and_tool_list_follow_the_protocol()
    {
        var run = await Server.RunAsync(new[] { "--registry", NewRegistry() }, new[]
        {
            Server.Rpc(1, "initialize", Initialize("2099-01-01")),
            Server.Rpc(2, "initialize", Initialize("2025-06-18")),
            Server.Rpc(3, "ping"),
            Server.Rpc(4, "resources/list"),
            Server.Rpc(5, "tools/list")
        });

        Assert.False(run.TimedOut, run.Error);

        // An unknown version is answered with the latest one the server knows; a known one is echoed.
        var first = run.Response(1).GetProperty("result");
        Assert.Equal("2025-11-25", first.GetProperty("protocolVersion").GetString());
        Assert.True(first.GetProperty("capabilities").GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.Equal("2025-06-18", run.Response(2).GetProperty("result").GetProperty("protocolVersion").GetString());

        Assert.Equal(JsonValueKind.Object, run.Response(3).GetProperty("result").ValueKind);
        Assert.Equal(0, run.Response(4).GetProperty("result").GetProperty("resources").GetArrayLength());

        var tools = run.Response(5).GetProperty("result").GetProperty("tools").EnumerateArray()
            .ToDictionary(tool => tool.GetProperty("name").GetString()!);
        Assert.Contains("AddMethod", tools.Keys);
        Assert.Contains("SwitchRegistry", tools.Keys);
        Assert.DoesNotContain("GetOriginStory", tools.Keys);
    }

    [Fact]
    public async Task The_server_stops_when_its_input_is_closed()
    {
        var run = await Server.RunAsync(new[] { "--registry", NewRegistry() }, Array.Empty<string>(), timeoutSeconds: 30);

        Assert.False(run.TimedOut, "The server kept running after standard input was closed.");
        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public async Task An_unsafe_registry_id_is_refused_at_startup()
    {
        var run = await Server.RunAsync(new[] { "--registry", "..\\evil" }, Array.Empty<string>(), timeoutSeconds: 30);

        Assert.False(run.TimedOut);
        Assert.Contains(run.Output, line => line.Contains("Invalid registry id"));
        Assert.False(Directory.Exists(Path.Combine(AppContext.BaseDirectory, "evil")));
    }

    [Fact]
    public async Task An_unreadable_index_is_refused_at_startup_and_left_untouched()
    {
        var registry = NewRegistry();
        Directory.CreateDirectory(TempRegistry.DataFolder(registry));
        var indexPath = Path.Combine(TempRegistry.DataFolder(registry), "__index__.json");
        const string corrupt = "[ {\"Key\": ";
        File.WriteAllText(indexPath, corrupt);

        var run = await Server.RunAsync(new[] { "--registry", registry }, Array.Empty<string>(), timeoutSeconds: 30);

        Assert.Contains(run.Output, line => line.Contains("unreadable"));
        Assert.Equal(corrupt, File.ReadAllText(indexPath));
    }

    [Fact]
    public async Task A_method_added_by_one_run_is_available_to_the_next()
    {
        var registry = NewRegistry();
        var args = new[] { "--registry", registry };

        var first = await Server.RunAsync(args, new[]
        {
            Server.ToolCall(1, "AddMethod", new
            {
                methodCode = "[McpMethod(\"Returns the square of an integer\")]\npublic int Square(int n) => n * n;"
            })
        });
        Assert.StartsWith("Successfully added Square", first.ToolText(1));

        var second = await Server.RunAsync(args, new[]
        {
            Server.Rpc(1, "tools/list"),
            Server.ToolCall(2, "Square", new { n = 7 })
        });

        var names = second.Response(1).GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString());
        Assert.Contains("Square", names);
        Assert.Equal("49", second.ToolText(2));
    }

    [Fact]
    public async Task Two_processes_writing_to_the_same_registry_lose_nothing()
    {
        // Claude Desktop starts two instances of the server, and both share the same data folder.
        var registry = NewRegistry();
        var args = new[] { "--registry", registry };
        const int perProcess = 25;

        static IEnumerable<string> Requests(string prefix) =>
            Enumerable.Range(1, perProcess).Select(i => Server.ToolCall(i, "SetData", new { key = $"{prefix}{i}", value = "v" }));

        var runs = await Task.WhenAll(Server.RunAsync(args, Requests("a")), Server.RunAsync(args, Requests("b")));

        foreach (var run in runs)
        {
            Assert.False(run.TimedOut, run.Error);
            for (var i = 1; i <= perProcess; i++)
                Assert.StartsWith("Data stored successfully", run.ToolText(i));
        }

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(TempRegistry.DataFolder(registry), "__index__.json")));
        Assert.Equal(2 * perProcess, index.RootElement.GetArrayLength());
    }
}
