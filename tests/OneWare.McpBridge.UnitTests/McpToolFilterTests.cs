using System.Text.Json;
using OneWare.McpBridge.Services;
using Xunit;

namespace OneWare.McpBridge.UnitTests;

public class McpToolFilterTests
{
    [Theory]
    [InlineData("fpga_compile", "fpga")]
    [InlineData("oneai_run_sam", "oneai")]
    [InlineData("getAllErrors", McpToolFilter.IdeGroup)]
    [InlineData("_leading", McpToolFilter.IdeGroup)]
    public void GetGroup_UsesPrefixBeforeFirstUnderscore(string tool, string group)
    {
        Assert.Equal(group, McpToolFilter.GetGroup(tool));
    }

    private static readonly string[] ToolNames = ["fpga_compile", "oneai_open_project", "getAllErrors"];

    [Theory]
    [InlineData("ONE AI rules: start with oneai_get_project_overview.", false)]
    [InlineData("Use fpga_list_projects first.", true)]
    [InlineData("Use fpga_compile, or oneai_open_project for vision.", true)]
    [InlineData("This project targets an FPGA.", true)]
    public void PromptAddition_IsDroppedOnlyWhenItNamesDisabledGroupsAlone(string addition, bool relevant)
    {
        Assert.Equal(relevant, McpToolFilter.IsPromptAdditionRelevant(addition, ToolNames, group => group != "oneai"));
    }

    [Theory]
    [InlineData("Error: No FPGA project is active.", true)]
    [InlineData("1 FPGA project(s) loaded", false)]
    [InlineData("""{"result":false,"error":"File does not exist"}""", true)]
    [InlineData("""{"result":true,"error":null}""", false)]
    [InlineData("""{"errors":["x"]}""", false)]
    public void IsErrorResult_DetectsOneWareErrorConventions(string json, bool isError)
    {
        object value = json.StartsWith('{') ? JsonDocument.Parse(json).RootElement : json;

        Assert.Equal(isError, OneWareToolHost.IsErrorResult(value));
    }

    [Fact]
    public void IsErrorResult_DetectsErrorTextMarshalledAsJsonString()
    {
        // AIFunctionFactory marshals a tool's string result to a JSON string element
        Assert.True(OneWareToolHost.IsErrorResult(JsonSerializer.SerializeToElement("Error: no project")));
        Assert.False(OneWareToolHost.IsErrorResult(JsonSerializer.SerializeToElement("ok")));
    }
}
