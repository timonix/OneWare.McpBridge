using Avalonia.Controls;
using Avalonia.Interactivity;
using OneWare.McpBridge.ViewModels;

namespace OneWare.McpBridge.Views;

public partial class McpBridgeView : UserControl
{
    public McpBridgeView()
    {
        InitializeComponent();
    }

    private void CopyClaudeCode_OnClick(object? sender, RoutedEventArgs e) =>
        Copy((DataContext as McpBridgeViewModel)?.ClaudeCodeConfig);

    private void CopyOpenCode_OnClick(object? sender, RoutedEventArgs e) =>
        Copy((DataContext as McpBridgeViewModel)?.OpenCodeConfig);

    private void CopyGeneric_OnClick(object? sender, RoutedEventArgs e) =>
        Copy((DataContext as McpBridgeViewModel)?.GenericConfig);

    private void Copy(string? text)
    {
        if (text != null) _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
    }
}
