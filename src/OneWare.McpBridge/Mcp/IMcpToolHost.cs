using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneWare.McpBridge.Mcp;

/// <summary>
/// The MCP client of a session, as it introduced itself in <c>initialize</c>.
/// </summary>
public sealed record McpClient(string SessionId, string Name, string? Version);

public sealed record McpToolInfo(string Name, string? Description, JsonElement InputSchema);

/// <summary>
/// The result of a tool call. <see cref="Content"/> holds MCP content blocks (text, image).
/// </summary>
public sealed record McpToolResult(IReadOnlyList<JsonObject> Content, bool IsError)
{
    public static McpToolResult Error(string message) => new([Text(message)], true);

    public static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };
}

/// <summary>
/// Provides the tools served over MCP. Kept separate from the protocol so the protocol can be tested without OneWare.
/// </summary>
public interface IMcpToolHost
{
    /// <summary>
    /// Instructions returned in <c>initialize</c>, shown to the model by most clients.
    /// </summary>
    string? Instructions { get; }

    IReadOnlyList<McpToolInfo> ListTools();

    /// <returns>Null if there is no tool with that name.</returns>
    Task<McpToolResult?> CallToolAsync(McpClient client, string name, JsonObject? arguments,
        CancellationToken cancellationToken);
}
