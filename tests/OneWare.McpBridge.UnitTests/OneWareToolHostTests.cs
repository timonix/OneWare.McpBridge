using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OneWare.McpBridge.Services;
using Xunit;

namespace OneWare.McpBridge.UnitTests;

public class OneWareToolHostTests
{
    [Fact]
    public void ToArgumentDictionary_ConvertsValuesToJsonElements()
    {
        var arguments = JsonNode.Parse("""{"path":"C:/a.vhd","line":3,"none":null}""") as JsonObject;

        var dictionary = OneWareToolHost.ToArgumentDictionary(arguments);

        Assert.Equal("C:/a.vhd", ((JsonElement)dictionary["path"]!).GetString());
        Assert.Equal(3, ((JsonElement)dictionary["line"]!).GetInt32());
        Assert.Null(dictionary["none"]);
    }

    [Fact]
    public async Task ToolResults_BindAndConvertThroughAIFunctionFactory()
    {
        var function = AIFunctionFactory.Create((string path, int? line) => new { path, line });
        var arguments = OneWareToolHost.ToArgumentDictionary(
            JsonNode.Parse("""{"path":"C:/a.vhd","line":3}""") as JsonObject);

        var value = await function.InvokeAsync(new AIFunctionArguments(arguments));
        var content = OneWareToolHost.ToContent(value);

        var text = Assert.Single(content)["text"]!.GetValue<string>();
        Assert.Contains("C:/a.vhd", text);
        Assert.Contains("3", text);
    }

    [Fact]
    public void ToContent_TurnsImagesIntoImageBlocks()
    {
        var content = OneWareToolHost.ToContent(new List<AIContent>
        {
            new TextContent("Image file"),
            new DataContent(new byte[] { 1, 2, 3 }, "image/png")
        });

        Assert.Equal("text", content[0]["type"]!.GetValue<string>());
        Assert.Equal("image", content[1]["type"]!.GetValue<string>());
        Assert.Equal("AQID", content[1]["data"]!.GetValue<string>());
        Assert.Equal("image/png", content[1]["mimeType"]!.GetValue<string>());
    }

    [Fact]
    public void ToContent_UnwrapsJsonStrings()
    {
        var content = OneWareToolHost.ToContent(JsonSerializer.SerializeToElement("plain"));

        Assert.Equal("plain", Assert.Single(content)["text"]!.GetValue<string>());
    }
}
