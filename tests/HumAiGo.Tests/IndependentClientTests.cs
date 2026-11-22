using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>
/// Talks to the server with the official MCP C# SDK, an implementation we did not write,
/// to check that the server follows the protocol and not just our own reading of it.
/// </summary>
public partial class IndependentClientTests : IDisposable
{
    private readonly string _registry = TempRegistry.NewId();

    public void Dispose() => TempRegistry.DeleteFolder(_registry);

    private Task<McpClient> ConnectAsync() => McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "humaigo-test",
        Command = "dotnet",
        Arguments = new[] { Path.Combine(AppContext.BaseDirectory, "humaigo.dll"), "--registry", _registry },
        WorkingDirectory = AppContext.BaseDirectory
    }));

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    [Fact]
    public async Task The_client_connects_and_reads_valid_tool_schemas()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        Assert.Equal("humaigo", client.ServerInfo?.Name);
        Assert.Contains(tools, tool => tool.Name == "AddMethod");
        Assert.All(tools, tool => Assert.Equal(JsonValueKind.Object, tool.JsonSchema.GetProperty("properties").ValueKind));

        var addMethod = tools.Single(tool => tool.Name == "AddMethod");
        var methodCode = addMethod.JsonSchema.GetProperty("properties").GetProperty("methodCode");
        Assert.False(string.IsNullOrWhiteSpace(methodCode.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task A_tool_added_through_the_client_can_be_called_at_once()
    {
        await using var client = await ConnectAsync();

        var added = await client.CallToolAsync("AddMethod", new Dictionary<string, object?>
        {
            ["methodCode"] = "[McpMethod(\"Returns the square of an integer\")]\npublic int Square(int n) => n * n;"
        });
        Assert.StartsWith("Successfully added Square", TextOf(added));

        var called = await client.CallToolAsync("Square", new Dictionary<string, object?> { ["n"] = 7 });
        Assert.Equal("49", TextOf(called));

        // The same method is reachable through the bridge tool, which exists because clients do not
        // refresh their tool list during a conversation.
        var bridged = await client.CallToolAsync("CallMethod", new Dictionary<string, object?>
        {
            ["methodName"] = "Square",
            ["argumentsJson"] = "{\"n\": 6}"
        });
        Assert.Equal("36", TextOf(bridged));
    }

    [Fact]
    public async Task An_unknown_tool_is_reported_as_an_error()
    {
        await using var client = await ConnectAsync();

        var outcome = await Record.ExceptionAsync(() => client.CallToolAsync("NoSuchTool", new Dictionary<string, object?>()).AsTask());

        // The server answers with a JSON-RPC error, which the SDK raises as an exception.
        Assert.NotNull(outcome);
    }
}
