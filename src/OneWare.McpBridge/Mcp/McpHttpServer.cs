using System.Net;
using System.Text;

namespace OneWare.McpBridge.Mcp;

/// <summary>
/// Serves <see cref="McpRequestHandler"/> on http://localhost:&lt;port&gt;/mcp.
/// </summary>
/// <remarks>
/// The listener is bound to localhost only, which needs no admin rights on Windows. Requests with a browser
/// Origin from anything but localhost are rejected, as the MCP spec asks, so web pages can't reach the tools
/// through DNS rebinding.
/// </remarks>
public sealed class McpHttpServer(McpRequestHandler handler) : IDisposable
{
    public const string EndpointPath = "/mcp";

    private HttpListener? _listener;
    private CancellationTokenSource? _cancellation;

    public bool IsRunning => _listener?.IsListening == true;

    public int Port { get; private set; }

    public string Url => $"http://localhost:{Port}{EndpointPath}";

    public event EventHandler<Exception>? RequestFailed;

    public void Start(int port)
    {
        Stop();

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        _listener = listener;
        _cancellation = new CancellationTokenSource();
        Port = port;

        _ = AcceptLoopAsync(listener, _cancellation.Token);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation = null;

        try
        {
            _listener?.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed
        }

        _listener = null;
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }

            // Tool calls can take minutes (compile, simulation), so each request runs on its own
            _ = Task.Run(() => ProcessAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task ProcessAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (!string.Equals(request.Url?.AbsolutePath.TrimEnd('/'), EndpointPath, StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(response, 404, null);
                return;
            }

            if (!IsAllowedOrigin(request.Headers["Origin"]))
            {
                await WriteAsync(response, 403, null);
                return;
            }

            var sessionId = request.Headers["Mcp-Session-Id"];

            switch (request.HttpMethod)
            {
                case "POST":
                    string body;
                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                        body = await reader.ReadToEndAsync(cancellationToken);

                    var result = await handler.HandleAsync(body, sessionId, cancellationToken);
                    if (result.SessionId != null) response.Headers["Mcp-Session-Id"] = result.SessionId;
                    await WriteAsync(response, result.StatusCode, result.Body);
                    break;
                case "DELETE":
                    if (sessionId != null) handler.EndSession(sessionId);
                    await WriteAsync(response, 200, null);
                    break;
                default:
                    // No server-initiated SSE stream is offered, the transport allows answering GET with 405
                    response.Headers["Allow"] = "POST, DELETE";
                    await WriteAsync(response, 405, null);
                    break;
            }
        }
        catch (Exception e)
        {
            RequestFailed?.Invoke(this, e);
            try
            {
                await WriteAsync(response, 500, null);
            }
            catch
            {
                // The connection is gone
            }
        }
    }

    internal static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback;
    }

    private static async Task WriteAsync(HttpListenerResponse response, int statusCode, string? body)
    {
        response.StatusCode = statusCode;
        if (body != null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
        }

        response.Close();
    }

    public void Dispose()
    {
        Stop();
    }
}
