using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumAiGo.McpServer.Models;

/// <summary>
/// An incoming JSON-RPC 2.0 message. A message without an id is a notification and gets no response.
/// </summary>
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public object? Params { get; set; }

    /// <summary>
    /// A JSON-RPC id is a number or a string. It is kept exactly as received and echoed back unchanged.
    /// </summary>
    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }
}
