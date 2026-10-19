using System.Collections.ObjectModel;
using System.Net;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using OneWare.Essentials.Services;
using OneWare.McpBridge.Mcp;

namespace OneWare.McpBridge.Services;

/// <summary>
/// Runs the MCP server and tracks which clients are connected.
/// </summary>
public sealed partial class McpBridgeService : ObservableObject, IDisposable
{
    public const string PortSetting = "McpBridge_Port";
    public const int DefaultPort = 3917;

    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;
    private readonly McpRequestHandler _handler;
    private readonly McpHttpServer _server;

    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private string _statusText = "Stopped";

    public McpBridgeService(IAiFunctionProvider functionProvider, McpActivity activity, McpToolFilter filter,
        ISettingsService settingsService, ILogger logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        var version = typeof(McpBridgeService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

        _handler = new McpRequestHandler(new OneWareToolHost(functionProvider, activity, filter), version);
        _handler.ClientConnected += (_, client) => Dispatcher.UIThread.Post(() => Clients.Add(client));
        _handler.ClientDisconnected += (_, client) => Dispatcher.UIThread.Post(() => Clients.Remove(client));

        _server = new McpHttpServer(_handler);
        _server.RequestFailed += (_, e) => _logger.Warning("MCP Bridge: a request failed", e, false);
    }

    /// <summary>
    /// Clients that connected since the server started. Clients that just stop without ending their
    /// session stay listed.
    /// </summary>
    public ObservableCollection<McpClient> Clients { get; } = [];

    public string Url => $"http://localhost:{Port}{McpHttpServer.EndpointPath}";

    private int Port => _settingsService.GetSettingValue<int>(PortSetting) is > 0 and < 65536 and var port
        ? port
        : DefaultPort;

    public void Start()
    {
        try
        {
            _server.Start(Port);
            IsRunning = true;
            StatusText = $"Listening on {Url}";
        }
        catch (HttpListenerException e)
        {
            IsRunning = false;
            StatusText = $"Could not listen on port {Port}: {e.Message}";
            _logger.Error($"MCP Bridge could not listen on port {Port}. Is another OneWare already running it?", e);
        }

        OnPropertyChanged(nameof(Url));
    }

    public void Stop()
    {
        _server.Stop();
        Clients.Clear();
        IsRunning = false;
        StatusText = "Stopped";
    }

    public void Dispose()
    {
        _server.Dispose();
    }
}
