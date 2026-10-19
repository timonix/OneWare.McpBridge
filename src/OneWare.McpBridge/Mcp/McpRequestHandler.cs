using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OneWare.McpBridge.Mcp;

public sealed record McpResponse(int StatusCode, string? Body, string? SessionId = null);

/// <summary>
/// Handles the JSON-RPC messages of the MCP Streamable HTTP transport. Only tools are offered, and every
/// request is answered with a plain JSON response (no SSE stream), which the transport allows.
/// </summary>
public sealed class McpRequestHandler(IMcpToolHost host, string serverVersion)
{
    // Newest first, the first one is answered when the client asks for a version we don't know
    public static readonly string[] SupportedProtocolVersions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    private const int ParseError = -32700;
    private const int InvalidRequest = -32600;
    private const int MethodNotFound = -32601;
    private const int InvalidParams = -32602;
    private const int InternalError = -32603;

    private readonly ConcurrentDictionary<string, McpClient> _sessions = new();

    public event EventHandler<McpClient>? ClientConnected;
    public event EventHandler<McpClient>? ClientDisconnected;

    public IReadOnlyCollection<McpClient> Clients => _sessions.Values.ToArray();

    public async Task<McpResponse> HandleAsync(string body, string? sessionId, CancellationToken cancellationToken)
    {
        JsonNode? message;
        try
        {
            message = JsonNode.Parse(body);
        }
        catch (JsonException e)
        {
            return new McpResponse(400, Error(null, ParseError, $"Parse error: {e.Message}").ToJsonString());
        }

        McpClient? client = null;
        if (sessionId != null && !_sessions.TryGetValue(sessionId, out client))
            // The spec tells clients to start a new session when they get a 404 for their session id
            return new McpResponse(404, Error(null, InvalidRequest, "Unknown or expired session.").ToJsonString());

        client ??= new McpClient(string.Empty, "unknown client", null);

        if (message is JsonArray batch)
        {
            var responses = new JsonArray();
            string? newSessionId = null;
            foreach (var item in batch)
            {
                var (response, createdSession) = await HandleMessageAsync(item, client, cancellationToken);
                if (response != null) responses.Add(response);
                newSessionId ??= createdSession;
            }

            return responses.Count == 0
                ? new McpResponse(202, null, newSessionId)
                : new McpResponse(200, responses.ToJsonString(), newSessionId);
        }

        var (single, sessionCreated) = await HandleMessageAsync(message, client, cancellationToken);
        return single == null
            ? new McpResponse(202, null, sessionCreated)
            : new McpResponse(200, single.ToJsonString(), sessionCreated);
    }

    public void EndSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var client))
            ClientDisconnected?.Invoke(this, client);
    }

    /// <returns>The response, or null for notifications and responses sent by the client.</returns>
    private async Task<(JsonObject? Response, string? CreatedSessionId)> HandleMessageAsync(JsonNode? node,
        McpClient client, CancellationToken cancellationToken)
    {
        if (node is not JsonObject message)
            return (Error(null, InvalidRequest, "A JSON-RPC message must be an object."), null);

        var id = message["id"]?.DeepClone();
        var method = message["method"]?.GetValue<string>();

        // Notifications (no id) and responses to server requests need no answer
        if (method == null || id == null) return (null, null);

        var parameters = message["params"] as JsonObject;

        try
        {
            switch (method)
            {
                case "initialize":
                    var session = Initialize(parameters);
                    return (Result(id, InitializeResult(parameters)), session.SessionId);
                case "ping":
                    return (Result(id, new JsonObject()), null);
                case "tools/list":
                    return (Result(id, ToolsListResult()), null);
                case "tools/call":
                    return (await CallToolAsync(id, parameters, client, cancellationToken), null);
                default:
                    return (Error(id, MethodNotFound, $"Method not found: {method}"), null);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (Error(id, InternalError, e.Message), null);
        }
    }

    private McpClient Initialize(JsonObject? parameters)
    {
        var clientInfo = parameters?["clientInfo"] as JsonObject;
        var client = new McpClient(Guid.NewGuid().ToString("N"),
            clientInfo?["name"]?.GetValue<string>() ?? "unknown client",
            clientInfo?["version"]?.GetValue<string>());

        _sessions[client.SessionId] = client;
        ClientConnected?.Invoke(this, client);
        return client;
    }

    private JsonObject InitializeResult(JsonObject? parameters)
    {
        var requested = parameters?["protocolVersion"]?.GetValue<string>();
        var version = SupportedProtocolVersions.Contains(requested) ? requested! : SupportedProtocolVersions[0];

        var result = new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "oneware",
                ["title"] = "OneWare Studio",
                ["version"] = serverVersion
            }
        };

        if (!string.IsNullOrWhiteSpace(host.Instructions)) result["instructions"] = host.Instructions;
        return result;
    }

    private JsonObject ToolsListResult()
    {
        var tools = new JsonArray();
        foreach (var tool in host.ListTools())
        {
            var entry = new JsonObject
            {
                ["name"] = tool.Name,
                ["inputSchema"] = ToInputSchema(tool.InputSchema)
            };
            if (!string.IsNullOrWhiteSpace(tool.Description)) entry["description"] = tool.Description;
            tools.Add(entry);
        }

        return new JsonObject { ["tools"] = tools };
    }

    /// <summary>
    /// MCP requires an object schema, also for tools without parameters.
    /// </summary>
    internal static JsonObject ToInputSchema(JsonElement schema)
    {
        var node = schema.ValueKind == JsonValueKind.Object ? JsonNode.Parse(schema.GetRawText()) as JsonObject : null;
        node ??= new JsonObject();
        node["type"] = "object";
        if (node["properties"] == null) node["properties"] = new JsonObject();
        return node;
    }

    private async Task<JsonObject> CallToolAsync(JsonNode id, JsonObject? parameters, McpClient client,
        CancellationToken cancellationToken)
    {
        var name = parameters?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name)) return Error(id, InvalidParams, "tools/call needs a tool name.");

        var result = await host.CallToolAsync(client, name, parameters?["arguments"] as JsonObject, cancellationToken);
        if (result == null) return Error(id, InvalidParams, $"Unknown tool: {name}");

        var content = new JsonArray();
        foreach (var block in result.Content) content.Add(block.DeepClone());

        return Result(id, new JsonObject { ["content"] = content, ["isError"] = result.IsError });
    }

    private static JsonObject Result(JsonNode id, JsonObject result)
    {
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    }

    private static JsonObject Error(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
    }
}
