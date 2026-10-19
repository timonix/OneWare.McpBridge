using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using OneWare.Essentials.Enums;
using OneWare.Essentials.Models;
using OneWare.Essentials.Services;
using OneWare.McpBridge.Services;
using OneWare.McpBridge.ViewModels;

namespace OneWare.McpBridge;

public class OneWareMcpBridgeModule : OneWareModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<McpActivity>();
        services.AddSingleton<McpToolFilter>();
        services.AddSingleton<McpBridgeService>();
        // Registered so a saved layout gets this instance back instead of a new one
        services.AddSingleton<McpBridgeViewModel>();
    }

    public override void Initialize(IServiceProvider serviceProvider)
    {
        serviceProvider.Resolve<ISettingsService>().RegisterSetting("Tools", "MCP Bridge", McpBridgeService.PortSetting,
            new TextBoxSetting("Port", McpBridgeService.DefaultPort, McpBridgeService.DefaultPort.ToString())
            {
                HoverDescription = "Port the MCP server listens on (http://localhost:<port>/mcp). " +
                                   "Takes effect after stopping and starting the server in the MCP Bridge window."
            });

        var dockService = serviceProvider.Resolve<IMainDockService>();
        dockService.RegisterLayoutExtension<McpBridgeViewModel>(DockShowLocation.Bottom);

        serviceProvider.Resolve<IWindowService>().RegisterMenuItem("MainWindow_MainMenu/View/Tool Windows",
            new MenuItemModel("McpBridge")
            {
                Header = "MCP Bridge",
                Command = new RelayCommand(() =>
                    dockService.Show(serviceProvider.Resolve<McpBridgeViewModel>(), DockShowLocation.Bottom)),
                Icon = new IconModel(McpBridgeViewModel.IconKey)
            });

        var bridge = serviceProvider.Resolve<McpBridgeService>();
        bridge.Start();
        serviceProvider.Resolve<IApplicationStateService>().RegisterShutdownAction(bridge.Dispose);
    }
}
