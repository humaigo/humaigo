using System.Text.Json;
using Xunit;

namespace HumAiGo.Tests;

/// <summary>How the server answers requests that are unusual, malformed, or that fail.</summary>
public class ProtocolTests : IDisposable
{
    private readonly List<string> _registries = new();

    private string[] NewRegistryArguments()
    {
        var id = TempRegistry.NewId();
        _registries.Add(id);
        return new[] { "--registry", id };
    }

    public void Dispose()
    {
        foreach (var id in _registries) TempRegistry.DeleteFolder(id);
    }

    private const string BoomCode = "[McpMethod(\"Always fails\")]\npublic int Boom(int n) { throw new System.InvalidOperationException(\"boom: \" + n); }";

    [Fact]
    public async Task A_request_id_may_be_a_string_and_is_echoed_back_unchanged()
    {
        var run = await Server.RunAsync(NewRegistryArguments(), new[]
        {
            "{\"jsonrpc\":\"2.0\",\"id\":\"request-abc\",\"method\":\"ping\"}"
        });

        var response = JsonDocument.Parse(run.Output.Single(line => line.Contains("request-abc"))).RootElement;
        Assert.Equal("request-abc", response.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Object, response.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task A_line_that_is_not_json_gets_a_parse_error_and_the_server_keeps_running()
    {
        var run = await Server.RunAsync(NewRegistryArguments(), new[]
        {
            "this is not json",
            Server.Rpc(1, "ping")
        });

        var parseError = run.Output
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Single(message => message.TryGetProperty("error", out _));
        Assert.Equal(-32700, parseError.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, parseError.GetProperty("id").ValueKind);

        Assert.Equal(JsonValueKind.Object, run.Response(1).GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task An_unknown_method_gets_method_not_found()
    {
        var run = await Server.RunAsync(NewRegistryArguments(), new[] { Server.Rpc(1, "no/such/method") });

        Assert.Equal(-32601, run.Response(1).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task An_unknown_tool_and_missing_arguments_are_protocol_errors()
    {
        var run = await Server.RunAsync(NewRegistryArguments(), new[]
        {
            Server.ToolCall(1, "NoSuchTool"),
            Server.ToolCall(2, "GetData", new { })
        });

        Assert.Equal(-32602, run.Response(1).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("Unknown tool", run.Response(1).GetProperty("error").GetProperty("message").GetString());

        Assert.Equal(-32602, run.Response(2).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("Missing parameter", run.Response(2).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_tool_that_throws_is_reported_in_its_result_with_its_own_message()
    {
        var run = await Server.RunAsync(NewRegistryArguments(), new[]
        {
            Server.ToolCall(1, "AddMethod", new { methodCode = BoomCode }),
            Server.ToolCall(2, "Boom", new { n = 7 }),
            Server.ToolCall(3, "CallMethod", new { methodName = "Boom", argumentsJson = "{\"n\": 8}" })
        });

        // Called directly: a tool error, flagged with isError, carrying the message the method threw.
        var direct = run.Response(2).GetProperty("result");
        Assert.True(direct.GetProperty("isError").GetBoolean());
        Assert.Equal("boom: 7", run.ToolText(2));

        // Called through the bridge tool: a plain result, with the same message and not the reflection wrapper's.
        Assert.False(run.Response(3).GetProperty("result").TryGetProperty("isError", out _));
        Assert.Contains("boom: 8", run.ToolText(3));
    }

    [Fact]
    public async Task Results_that_are_not_text_are_returned_as_json()
    {
        var run = await Server.RunAsync(NewRegistryArguments(), new[]
        {
            Server.ToolCall(1, "AddMethod", new
            {
                methodCode = "[McpMethod(\"Is the number even\")]\npublic bool IsEven(int n) => n % 2 == 0;"
            }),
            Server.ToolCall(2, "IsEven", new { n = 4 })
        });

        Assert.Equal("true", run.ToolText(2));
    }
}
