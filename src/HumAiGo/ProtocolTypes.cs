using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumAiGo;

/// <summary>The parameters of an MCP "initialize" request.</summary>
internal class InitializeParams
{
    public string ProtocolVersion { get; set; } = "";
    public object? Capabilities { get; set; }
    public ClientInfo? ClientInfo { get; set; }
}

internal class ClientInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
}

/// <summary>The parameters of an MCP "tools/call" request: the tool name and its arguments, as a JSON object.</summary>
internal class ToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; set; }
}
