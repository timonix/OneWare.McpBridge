using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OneWare.McpBridge.Mcp;

namespace OneWare.McpBridge.Services;

/// <summary>
/// What the MCP clients are doing: the call log and the tool calls waiting for the user's approval.
/// Shown in the MCP Bridge tab. All collections are only changed on the UI thread.
/// </summary>
public sealed class McpActivity
{
    private const int MaxCalls = 500;

    // Nobody may be at the IDE, so an unanswered request is denied instead of blocking the client forever
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMinutes(5);

    // "Allow for this session" approvals, per MCP session and tool
    private readonly HashSet<(string SessionId, string Tool)> _sessionApprovals = [];

    public ObservableCollection<McpCallEntry> Calls { get; } = [];

    public ObservableCollection<McpConfirmationRequest> Confirmations { get; } = [];

    public event EventHandler? ConfirmationRequested;

    public async Task<McpCallEntry> AddCallAsync(McpClient client, string tool, string arguments)
    {
        var entry = new McpCallEntry(DateTime.Now, client.Name, tool, arguments);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Calls.Insert(0, entry);
            while (Calls.Count > MaxCalls) Calls.RemoveAt(Calls.Count - 1);
        });
        return entry;
    }

    public void ClearCalls()
    {
        Dispatcher.UIThread.Post(() => Calls.Clear());
    }

    /// <returns>True if the user allowed the call.</returns>
    public async Task<bool> RequestConfirmationAsync(McpClient client, string tool, string message,
        CancellationToken cancellationToken)
    {
        var approvalKey = (client.SessionId, tool);
        if (await Dispatcher.UIThread.InvokeAsync(() => _sessionApprovals.Contains(approvalKey))) return true;

        var request = new McpConfirmationRequest(client.Name, tool, message);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Confirmations.Add(request);
            ConfirmationRequested?.Invoke(this, EventArgs.Empty);
        });

        try
        {
            var decision = await request.Decision.WaitAsync(ConfirmationTimeout, cancellationToken);
            if (decision == McpConfirmationDecision.AllowForSession)
                await Dispatcher.UIThread.InvokeAsync(() => _sessionApprovals.Add(approvalKey));
            return decision != McpConfirmationDecision.Deny;
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => Confirmations.Remove(request));
        }
    }
}

public enum McpConfirmationDecision
{
    Allow,
    AllowForSession,
    Deny
}

public sealed partial class McpConfirmationRequest(string client, string tool, string message)
{
    private readonly TaskCompletionSource<McpConfirmationDecision> _decision =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Client { get; } = client;

    public string Tool { get; } = tool;

    public string Message { get; } = message;

    public string Header => $"{Client} wants to run {Tool}";

    public Task<McpConfirmationDecision> Decision => _decision.Task;

    [RelayCommand]
    private void Allow() => _decision.TrySetResult(McpConfirmationDecision.Allow);

    [RelayCommand]
    private void AllowForSession() => _decision.TrySetResult(McpConfirmationDecision.AllowForSession);

    [RelayCommand]
    private void Deny() => _decision.TrySetResult(McpConfirmationDecision.Deny);
}

public sealed partial class McpCallEntry(DateTime started, string client, string tool, string arguments)
    : ObservableObject
{
    public DateTime Started { get; } = started;

    public string Client { get; } = client;

    public string Tool { get; } = tool;

    public string Arguments { get; } = arguments;

    [ObservableProperty] private bool _isRunning = true;

    [ObservableProperty] private bool _success;

    [ObservableProperty] private string _duration = string.Empty;

    [ObservableProperty] private string _result = string.Empty;

    public string StateText => IsRunning ? "…" : Success ? "✓" : "✗";

    public void Complete(bool success, string result, TimeSpan duration)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Success = success;
            Result = result;
            Duration = duration.TotalSeconds >= 1 ? $"{duration.TotalSeconds:0.0} s" : $"{duration.TotalMilliseconds:0} ms";
            IsRunning = false;
            OnPropertyChanged(nameof(StateText));
        });
    }
}
