using System.Text.Json.Serialization;

namespace HumAiGo.McpServer.Models;

/// <summary>The content of humaigo_config.json, read next to the executable.</summary>
public class HumAiGoConfig
{
    /// <summary>The registry (toolbox) used at startup unless --registry or HUMAIGO_REGISTRY says otherwise.</summary>
    [JsonPropertyName("registryid")]
    public string RegistryId { get; set; } = string.Empty;
}
