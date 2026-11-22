using System;
using System.Collections.Generic;

namespace HumAiGo.McpServer.Models;

/// <summary>One line of a registry index: a key, and the name of the file that holds its value.</summary>
public class IndexEntry
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Name of the file, in the registry folder, that contains the value.</summary>
    public string ValueKey { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }
    public List<string> Tags { get; set; } = new();
}
