using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Microsoft.Extensions.AI;
using OneWare.Essentials.Services;
using OneWare.McpBridge.Mcp;

namespace OneWare.McpBridge.Services;

/// <summary>
/// Serves the tools registered in OneWare's <see cref="IAiFunctionProvider"/> (built-in IDE tools, FPGA tools,
/// tools of other plugins) to MCP clients.
/// </summary>
/// <remarks>
/// OneWare's chat shows a tool's confirmation prompt itself, invoking the tool does not. So it is done here,
/// in the MCP Bridge tab.
/// </remarks>
public sealed class OneWareToolHost(IAiFunctionProvider functionProvider, McpActivity activity, McpToolFilter filter)
    : IMcpToolHost
{
    private const int MaxLoggedResultLength = 4000;

    public string? Instructions
    {
        get
        {
            var toolNames = functionProvider.GetTools().Select(x => x.Name).ToList();
            var additions = functionProvider.GetPromptAdditions()
                .Where(x => filter.IsPromptAdditionRelevant(x, toolNames));
            return string.Join("\n\n",
                new[]
                {
                    "These tools control the OneWare Studio IDE the user has open: its projects, FPGA toolchains, " +
                    "simulations, open editors and the errors its language servers report. " +
                    "Paths are absolute paths on the user's machine."
                }.Concat(additions));
        }
    }

    public IReadOnlyList<McpToolInfo> ListTools()
    {
        return GetVisibleTools()
            .Select(x => new McpToolInfo(x.Name, x.Description, x.JsonSchema))
            .ToList();
    }

    public async Task<McpToolResult?> CallToolAsync(McpClient client, string name, JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        var function = GetVisibleTools().FirstOrDefault(x => x.Name == name);
        if (function == null) return null;

        var functionArguments = new AIFunctionArguments(ToArgumentDictionary(arguments));
        var entry = await activity.AddCallAsync(client, name, arguments?.ToJsonString() ?? "{}");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (functionProvider.GetConfirmationCheck(name) is { } confirmationCheck)
            {
                var message = await Dispatcher.UIThread.InvokeAsync(() => confirmationCheck(functionArguments));
                if (message != null &&
                    !await activity.RequestConfirmationAsync(client, name, message, cancellationToken))
                {
                    entry.Complete(false, "Denied in OneWare.", stopwatch.Elapsed);
                    return McpToolResult.Error("The user denied this tool call in OneWare.");
                }
            }

            var value = await function.InvokeAsync(functionArguments, cancellationToken);
            var result = new McpToolResult(ToContent(value), IsErrorResult(value));
            entry.Complete(!result.IsError, Summarize(result), stopwatch.Elapsed);
            return result;
        }
        catch (Exception e)
        {
            entry.Complete(false, e.Message, stopwatch.Elapsed);
            if (e is OperationCanceledException) throw;
            return McpToolResult.Error($"{name} failed: {e.Message}");
        }
    }

    private IEnumerable<AIFunction> GetVisibleTools()
    {
        return functionProvider.GetTools().Where(x => filter.IsEnabled(x.Name));
    }

    /// <summary>
    /// OneWare tools report failures as a result, not an exception: the FPGA tools as text starting with
    /// "Error:", the built-in tools as an object with a non-empty "error" field.
    /// </summary>
    internal static bool IsErrorResult(object? value)
    {
        return value switch
        {
            string text => text.StartsWith("Error:", StringComparison.Ordinal),
            JsonElement { ValueKind: JsonValueKind.String } text =>
                text.GetString()?.StartsWith("Error:", StringComparison.Ordinal) == true,
            JsonElement { ValueKind: JsonValueKind.Object } json =>
                json.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(error.GetString()),
            _ => false
        };
    }

    internal static Dictionary<string, object?> ToArgumentDictionary(JsonObject? arguments)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (arguments == null) return dictionary;

        // AIFunctionFactory binds JsonElement values to the handler's parameter types
        foreach (var (key, value) in arguments)
            dictionary[key] = value == null ? null : JsonSerializer.SerializeToElement(value);

        return dictionary;
    }

    internal static IReadOnlyList<JsonObject> ToContent(object? value)
    {
        return value switch
        {
            null => [McpToolResult.Text("Done.")],
            AIContent content => [ToContentBlock(content)],
            IEnumerable<AIContent> contents => contents.Select(ToContentBlock).ToList(),
            JsonElement { ValueKind: JsonValueKind.String } text => [McpToolResult.Text(text.GetString() ?? string.Empty)],
            JsonElement json => [McpToolResult.Text(json.GetRawText())],
            string text => [McpToolResult.Text(text)],
            _ => [McpToolResult.Text(JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions))]
        };
    }

    private static JsonObject ToContentBlock(AIContent content)
    {
        return content switch
        {
            TextContent text => McpToolResult.Text(text.Text),
            DataContent data when data.HasTopLevelMediaType("image") => new JsonObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(data.Data.Span),
                ["mimeType"] = data.MediaType
            },
            _ => McpToolResult.Text(content.ToString() ?? string.Empty)
        };
    }

    private static string Summarize(McpToolResult result)
    {
        var text = string.Join("\n", result.Content.Select(x =>
            x["type"]?.GetValue<string>() == "text" ? x["text"]?.GetValue<string>() : $"[{x["type"]}]"));

        return text.Length > MaxLoggedResultLength ? text[..MaxLoggedResultLength] + " …" : text;
    }
}
