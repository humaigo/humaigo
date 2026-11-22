using System;

namespace HumAiGo.McpServer.Models;

/// <summary>A value read from the store, with its description and the time it was written.</summary>
public class EntryValue
{
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
