using System.Text.Json;
using System.Text.Json.Nodes;
using OneWare.McpBridge.Mcp;
using Xunit;

namespace OneWare.McpBridge.UnitTests;

public class McpRequestHandlerTests
{
    private sealed class FakeToolHost : IMcpToolHost
    {
        public McpClient? LastClient { get; private set; }
        public JsonObject? LastArguments { get; private set; }

        public string? Instructions => "Use the IDE tools.";

        public IReadOnlyList<McpToolInfo> ListTools() =>
        [
            new("echo", "Echoes its input", JsonDocument.Parse("""{"type":"object","properties":{"text":{"type":"string"}}}""").RootElement),
            new("noParameters", null, default)
        ];

        public Task<McpToolResult?> CallToolAsync(McpClient client, string name, JsonObject? arguments,
            CancellationToken cancellationToken)
        {
            LastClient = client;
            LastArguments = arguments;
            return Task.FromResult<McpToolResult?>(name == "echo"
                ? new McpToolResult([McpToolResult.Text(arguments?["text"]?.GetValue<string>() ?? "")], false)
                : null);
        }
    }

    private readonly FakeToolHost _host = new();
    private readonly McpRequestHandler _handler;

    public McpRequestHandlerTests()
    {
        _handler = new McpRequestHandler(_host, "0.1.45");
    }

    private async Task<(McpResponse Response, JsonNode? Json)> SendAsync(string body, string? sessionId = null)
    {
        var response = await _handler.HandleAsync(body, sessionId, CancellationToken.None);
        return (response, response.Body == null ? null : JsonNode.Parse(response.Body));
    }

    private async Task<string> InitializeAsync(string protocolVersion = "2025-06-18")
    {
        var (response, _) = await SendAsync($$$$"""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"{{{{protocolVersion}}}}",
             "capabilities":{},"clientInfo":{"name":"opencode","version":"1.0"}}}
            """);
        return response.SessionId!;
    }

    [Fact]
    public async Task Initialize_CreatesSessionAndEchoesKnownProtocolVersion()
    {
        var (response, json) = await SendAsync("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26",
             "clientInfo":{"name":"claude-code","version":"2.0"}}}
            """);

        Assert.Equal(200, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(response.SessionId));
        Assert.Equal("2025-03-26", json!["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("oneware", json["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal("Use the IDE tools.", json["result"]!["instructions"]!.GetValue<string>());
        Assert.Contains(_handler.Clients, x => x.Name == "claude-code");
    }

    [Fact]
    public async Task Initialize_AnswersNewestVersionForUnknownProtocolVersion()
    {
        var (_, json) = await SendAsync("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}
            """);

        Assert.Equal(McpRequestHandler.SupportedProtocolVersions[0],
            json!["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task Notification_IsAcceptedWithoutBody()
    {
        var session = await InitializeAsync();
        var (response, _) = await SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", session);

        Assert.Equal(202, response.StatusCode);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task ToolsList_ReturnsObjectSchemaForEveryTool()
    {
        var session = await InitializeAsync();
        var (_, json) = await SendAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", session);

        var tools = json!["result"]!["tools"]!.AsArray();
        Assert.Equal(2, tools.Count);
        Assert.Equal("echo", tools[0]!["name"]!.GetValue<string>());
        Assert.Equal("string", tools[0]!["inputSchema"]!["properties"]!["text"]!["type"]!.GetValue<string>());
        Assert.Equal("object", tools[1]!["inputSchema"]!["type"]!.GetValue<string>());
        Assert.NotNull(tools[1]!["inputSchema"]!["properties"]);
        Assert.Null(tools[1]!["description"]);
    }

    [Fact]
    public async Task ToolsCall_PassesArgumentsAndClient()
    {
        var session = await InitializeAsync();
        var (_, json) = await SendAsync("""
            {"jsonrpc":"2.0","id":"call-1","method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}
            """, session);

        Assert.Equal("call-1", json!["id"]!.GetValue<string>());
        Assert.False(json["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("hi", json["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("opencode", _host.LastClient!.Name);
    }

    [Fact]
    public async Task ToolsCall_UnknownToolIsInvalidParams()
    {
        var session = await InitializeAsync();
        var (_, json) = await SendAsync("""
            {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"missing"}}
            """, session);

        Assert.Equal(-32602, json!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnknownMethod_IsMethodNotFound()
    {
        var (_, json) = await SendAsync("""{"jsonrpc":"2.0","id":4,"method":"resources/list"}""");

        Assert.Equal(-32601, json!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task UnknownSession_Is404()
    {
        var (response, _) = await SendAsync("""{"jsonrpc":"2.0","id":5,"method":"ping"}""", "not-a-session");

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task EndedSession_Is404()
    {
        var session = await InitializeAsync();
        _handler.EndSession(session);
        var (response, _) = await SendAsync("""{"jsonrpc":"2.0","id":6,"method":"ping"}""", session);

        Assert.Equal(404, response.StatusCode);
        Assert.Empty(_handler.Clients);
    }

    [Fact]
    public async Task InvalidJson_IsParseError()
    {
        var (response, json) = await SendAsync("{not json");

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(-32700, json!["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Batch_AnswersRequestsAndSkipsNotifications()
    {
        var session = await InitializeAsync();
        var (response, json) = await SendAsync("""
            [{"jsonrpc":"2.0","method":"notifications/initialized"},
             {"jsonrpc":"2.0","id":7,"method":"ping"},
             {"jsonrpc":"2.0","id":8,"method":"tools/list"}]
            """, session);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(2, json!.AsArray().Count);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("https://evil.example", false)]
    [InlineData("null", false)]
    public void Origin_OnlyLoopbackIsAllowed(string? origin, bool allowed)
    {
        Assert.Equal(allowed, McpHttpServer.IsAllowedOrigin(origin));
    }
}
