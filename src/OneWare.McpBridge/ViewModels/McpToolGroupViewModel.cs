using CommunityToolkit.Mvvm.ComponentModel;
using OneWare.McpBridge.Services;

namespace OneWare.McpBridge.ViewModels;

public sealed class McpToolGroupViewModel : ObservableObject
{
    private readonly McpToolFilter _filter;

    public McpToolGroupViewModel(string group, IEnumerable<(string Name, string? Description)> tools,
        McpToolFilter filter)
    {
        _filter = filter;
        Group = group;
        Tools = tools.Select(x => new McpToolItemViewModel(x.Name, x.Description, filter)).ToList();
    }

    public string Group { get; }

    public string Header => Group == McpToolFilter.IdeGroup
        ? $"IDE built-ins ({Tools.Count})"
        : $"{Group}_* ({Tools.Count})";

    public IReadOnlyList<McpToolItemViewModel> Tools { get; }

    public bool IsEnabled
    {
        get => _filter.IsGroupEnabled(Group);
        set
        {
            _filter.SetGroupEnabled(Group, value);
            OnPropertyChanged();
        }
    }
}

public sealed class McpToolItemViewModel(string name, string? description, McpToolFilter filter) : ObservableObject
{
    public string Name { get; } = name;

    public string? Description { get; } = description;

    public bool IsEnabled
    {
        get => filter.IsToolEnabled(Name);
        set
        {
            filter.SetToolEnabled(Name, value);
            OnPropertyChanged();
        }
    }
}
