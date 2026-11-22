using System;

namespace HumAiGo.McpServer.Models;

/// <summary>
/// Marks a public method as an MCP tool. The description is what the calling assistant reads
/// to decide when to use it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class McpMethodAttribute : Attribute
{
    public string Description { get; }

    public McpMethodAttribute(string description)
    {
        Description = description;
    }
}
