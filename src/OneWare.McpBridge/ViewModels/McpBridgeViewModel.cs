using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Services;
using OneWare.Essentials.ViewModels;
using OneWare.McpBridge.Services;

namespace OneWare.McpBridge.ViewModels;

public partial class McpBridgeViewModel : ExtendedTool
{
    public const string IconKey = "VSImageLib2019.Plugged_16x";

    private readonly IAiFunctionProvider _functionProvider;
    private readonly McpToolFilter _filter;

    public McpBridgeViewModel(McpBridgeService bridge, McpActivity activity, McpToolFilter filter,
        IAiFunctionProvider functionProvider, IMainDockService dockService)
        : base(IconKey)
    {
        Id = "McpBridge";
        Title = "MCP Bridge";

        Bridge = bridge;
        Activity = activity;
        _filter = filter;
        _functionProvider = functionProvider;

        // A waiting tool call is easy to miss when the tab is in the background
        activity.ConfirmationRequested += (_, _) => dockService.Show(this, DockShowLocation.Bottom);
    }

    public McpBridgeService Bridge { get; }

    public McpActivity Activity { get; }

    public string ClaudeCodeConfig =>
        $"claude mcp add --transport http oneware {Bridge.Url}";

    public string OpenCodeConfig =>
        $$"""
          "mcp": {
            "oneware": { "type": "remote", "url": "{{Bridge.Url}}", "enabled": true, "oauth": false }
          }
          """;

    public string GenericConfig =>
        $$"""
          { "mcpServers": { "oneware": { "type": "http", "url": "{{Bridge.Url}}" } } }
          """;

    [ObservableProperty] private bool _showTools;

    [ObservableProperty] private IReadOnlyList<McpToolGroupViewModel> _toolGroups = [];

    partial void OnShowToolsChanged(bool value)
    {
        if (value) RefreshTools();
    }

    /// <summary>
    /// Plugins can register tools at any time, so the list is read again whenever it is opened.
    /// </summary>
    [RelayCommand]
    private void RefreshTools()
    {
        ToolGroups = _functionProvider.GetTools()
            .GroupBy(x => McpToolFilter.GetGroup(x.Name))
            .OrderBy(x => x.Key == McpToolFilter.IdeGroup ? 0 : 1).ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new McpToolGroupViewModel(x.Key, x.Select(t => (t.Name, (string?)t.Description)), _filter))
            .ToList();
    }

    [RelayCommand]
    private void ToggleServer()
    {
        if (Bridge.IsRunning) Bridge.Stop();
        else Bridge.Start();
    }

    [RelayCommand]
    private void ClearCalls() => Activity.ClearCalls();
}
