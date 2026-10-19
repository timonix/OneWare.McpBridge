using OneWare.Essentials.Services;

namespace OneWare.McpBridge.Services;

/// <summary>
/// Which OneWare tools are served over MCP. Tools are grouped by their name prefix (<c>fpga_*</c>, <c>oneai_*</c>),
/// the built-in IDE tools have none. A tool is served if its group and the tool itself are enabled.
/// </summary>
public sealed class McpToolFilter
{
    public const string DisabledGroupsSetting = "McpBridge_DisabledGroups";
    public const string DisabledToolsSetting = "McpBridge_DisabledTools";

    public const string IdeGroup = "ide";

    // Coding agents bring their own file and shell tools, which work better outside the IDE
    public const string DefaultDisabledTools = "readFile,editFile,runTerminalCommand";

    private readonly ISettingsService _settingsService;

    public McpToolFilter(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        settingsService.Register(DisabledGroupsSetting, string.Empty);
        settingsService.Register(DisabledToolsSetting, DefaultDisabledTools);
    }

    public event EventHandler? Changed;

    /// <summary>
    /// The group of a tool: the prefix before the first underscore, or <see cref="IdeGroup"/> without one.
    /// </summary>
    public static string GetGroup(string toolName)
    {
        var separator = toolName.IndexOf('_');
        return separator > 0 ? toolName[..separator] : IdeGroup;
    }

    public bool IsEnabled(string toolName)
    {
        return IsGroupEnabled(GetGroup(toolName)) && IsToolEnabled(toolName);
    }

    public bool IsGroupEnabled(string group) => !Read(DisabledGroupsSetting).Contains(group);

    public bool IsToolEnabled(string toolName) => !Read(DisabledToolsSetting).Contains(toolName);

    public void SetGroupEnabled(string group, bool enabled) => Write(DisabledGroupsSetting, group, !enabled);

    public void SetToolEnabled(string toolName, bool enabled) => Write(DisabledToolsSetting, toolName, !enabled);

    /// <summary>
    /// Prompt additions belong to no group, so one is left out when it names tools of a disabled group
    /// (e.g. "oneai_") and none of an enabled group. Otherwise a disabled plugin still fills the instructions.
    /// </summary>
    public bool IsPromptAdditionRelevant(string promptAddition, IEnumerable<string> toolNames)
    {
        return IsPromptAdditionRelevant(promptAddition, toolNames, IsGroupEnabled);
    }

    internal static bool IsPromptAdditionRelevant(string promptAddition, IEnumerable<string> toolNames,
        Func<string, bool> isGroupEnabled)
    {
        var groups = toolNames.Select(GetGroup).Where(x => x != IdeGroup).Distinct().ToList();
        var mentioned = groups.Where(x => promptAddition.Contains(x + "_", StringComparison.Ordinal)).ToList();
        return mentioned.Count == 0 || mentioned.Any(isGroupEnabled);
    }

    private HashSet<string> Read(string setting)
    {
        var value = _settingsService.GetSettingValue<string>(setting) ?? string.Empty;
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void Write(string setting, string name, bool disabled)
    {
        var values = Read(setting);
        if (!(disabled ? values.Add(name) : values.Remove(name))) return;

        _settingsService.SetSettingValue(setting, string.Join(",", values.Order(StringComparer.Ordinal)));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
