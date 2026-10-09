using System;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.DetectAspectRatio.Transformations;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class WebClientFunctionsTests
{
    private const string Entry = "/DetectAspectRatio/Client/detectAspectRatio.js?v=0123456789abcdef";
    private const string HeadCloseTag = "</head>";
    private const string ModuleScriptStart = "<script type=\"module\" " + WebClientFunctions.BootstrapMarker + ">";
    private const string ClassicScriptStart = "<script " + WebClientFunctions.BootstrapMarker + ">";
    private const char LineSeparator = (char)0x2028;
    private const char LatinSmallEWithAcute = (char)0xE9;
    private const string Page = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><script defer=\"defer\" src=\"main.jellyfin.bundle.js\"></script></head><body><div id=\"reactRoot\"></div></body></html>";
    private const string Config = """
        {
          "multiserver": false,
          "themes": [{ "name": "Dark", "id": "dark", "default": true }],
          "menuLinks": [],
          "ratio": 1.50,
          "plugins": ["htmlVideoPlayer/plugin", "syncPlay/plugin"]
        }
        """;

    [Fact]
    public void InjectBootstrapScript_InsertsTheScriptsImmediatelyBeforeHeadEnd()
    {
        string result = WebClientFunctions.InjectBootstrapScript(Page, Entry);

        string scripts = WebClientFunctions.BuildBootstrapScript(Entry);
        Assert.Equal(Page.Insert(Page.IndexOf(HeadCloseTag, StringComparison.Ordinal), scripts), result);
        Assert.Equal(2, CountOccurrences(result, WebClientFunctions.BootstrapMarker));
        Assert.StartsWith(ModuleScriptStart, scripts, StringComparison.Ordinal);
        Assert.EndsWith("</script></head><body><div id=\"reactRoot\"></div></body></html>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectBootstrapScript_MatchesHeadEndCaseInsensitivelyAndUsesTheFirst()
    {
        const string UpperCasePage = "<HTML><HEAD><TITLE>x</TITLE></HEAD><BODY><template></head></template></BODY></HTML>";

        string result = WebClientFunctions.InjectBootstrapScript(UpperCasePage, Entry);

        Assert.Equal(UpperCasePage.IndexOf("</HEAD>", StringComparison.Ordinal), result.IndexOf("<script ", StringComparison.Ordinal));
        Assert.EndsWith("</script></HEAD><BODY><template></head></template></BODY></HTML>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectBootstrapScript_AlreadyInjected_ReturnsInputUnchanged()
    {
        string once = WebClientFunctions.InjectBootstrapScript(Page, Entry);

        string twice = WebClientFunctions.InjectBootstrapScript(once, "/DetectAspectRatio/Client/detectAspectRatio.js?v=other");

        Assert.Same(once, twice);
    }

    [Fact]
    public void InjectBootstrapScript_WithoutHeadEnd_ReturnsInputUnchanged()
    {
        const string HeadlessPage = "<html><body>no head end tag</body></html>";

        Assert.Same(HeadlessPage, WebClientFunctions.InjectBootstrapScript(HeadlessPage, Entry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void InjectBootstrapScript_WithoutAddon_ReturnsInputUnchanged(string? entry)
    {
        Assert.Same(Page, WebClientFunctions.InjectBootstrapScript(Page, entry));
    }

    [Fact]
    public void BuildBootstrapScript_ImportsOnlyFromTheModuleScript()
    {
        string scripts = WebClientFunctions.BuildBootstrapScript(Entry);

        // Older TV browsers skip module scripts, so only the module script may spell out import()
        int classicStart = scripts.IndexOf(ClassicScriptStart, StringComparison.Ordinal);
        string moduleScript = scripts[..classicStart];
        string classicScript = scripts[classicStart..];
        Assert.Equal("<script type=\"module\" data-detect-aspect-ratio-bootstrap>window.DetectAspectRatioImport = function (url) { return import(url); };</script>", moduleScript);
        Assert.DoesNotContain("import(", classicScript, StringComparison.Ordinal);
        Assert.DoesNotContain("const ", classicScript, StringComparison.Ordinal);
        Assert.DoesNotContain("let ", classicScript, StringComparison.Ordinal);
        Assert.DoesNotContain("=>", classicScript, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBootstrapScript_CarriesOnlyLoadSettingsAndContractNames()
    {
        string scripts = WebClientFunctions.BuildBootstrapScript(Entry);

        JsonObject settings = ReadSettings(scripts);
        Assert.Equal(2, settings.Count);
        Assert.Equal(Entry, (string?)settings["entry"]);
        Assert.Equal(WebClientFunctions.AddonLoadTimeoutMilliseconds, (int?)settings["loadTimeout"]);
        Assert.Contains("pathname.lastIndexOf('/web/')", scripts, StringComparison.Ordinal);
        Assert.Contains("var importModule = window.DetectAspectRatioImport;", scripts, StringComparison.Ordinal);
        Assert.Contains("importModule(base + settings.entry)", scripts, StringComparison.Ordinal);
        Assert.Contains("window.DetectAspectRatio = function () {", scripts, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('DOMContentLoaded'", scripts, StringComparison.Ordinal);
        Assert.Contains("return UnavailableAddon;", scripts, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBootstrapScript_EscapesValuesSoTheInlineScriptCannotEndEarly()
    {
        string hostileEntry = "x</script><script>alert(1)</script><!--'\"&" + LineSeparator + LatinSmallEWithAcute + ".js";

        string scripts = WebClientFunctions.BuildBootstrapScript(hostileEntry);

        string classicScript = scripts[scripts.IndexOf(ClassicScriptStart, StringComparison.Ordinal)..];
        string body = classicScript[ClassicScriptStart.Length..classicScript.LastIndexOf("</script>", StringComparison.Ordinal)];
        Assert.False(body.Contains('<', StringComparison.Ordinal));
        Assert.False(body.Contains(LineSeparator, StringComparison.Ordinal));
        Assert.Equal(hostileEntry, (string?)ReadSettings(scripts)["entry"]);
    }

    [Fact]
    public void AddPluginToConfig_AppendsToExistingPluginsAndPreservesTheRest()
    {
        string result = WebClientFunctions.AddPluginToConfig(Config);

        JsonNode? expected = JsonNode.Parse(Config);
        expected!["plugins"]!.AsArray().Add(JsonValue.Create(WebClientFunctions.ClientPluginName));
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(result)));
        Assert.Equal(
            ["htmlVideoPlayer/plugin", "syncPlay/plugin", WebClientFunctions.ClientPluginName],
            JsonNode.Parse(result)!["plugins"]!.AsArray().Select(plugin => (string?)plugin));
        Assert.Contains("\"ratio\": 1.50", result, StringComparison.Ordinal);
        Assert.False(result.Contains('\r', StringComparison.Ordinal));
    }

    [Fact]
    public void AddPluginToConfig_KeepsNonASCIITextLiteral()
    {
        string serverName = "Caf" + LatinSmallEWithAcute;
        string config = "{\"servers\":[\"" + serverName + "\"],\"plugins\":[]}";

        string result = WebClientFunctions.AddPluginToConfig(config);

        Assert.Contains("\"" + serverName + "\"", result, StringComparison.Ordinal);
        Assert.Equal(WebClientFunctions.ClientPluginName, (string?)JsonNode.Parse(result)!["plugins"]![0]);
    }

    [Fact]
    public void AddPluginToConfig_AlreadyListed_ReturnsInputUnchanged()
    {
        const string ListedConfig = "{\"plugins\":[\"htmlVideoPlayer/plugin\",\"DetectAspectRatio\"]}";

        Assert.Same(ListedConfig, WebClientFunctions.AddPluginToConfig(ListedConfig));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"multiserver\":false,\"themes\":[]}")]
    [InlineData("[\"htmlVideoPlayer/plugin\"]")]
    [InlineData("null")]
    public void AddPluginToConfig_MissingPlugins_ReturnsInputUnchanged(string config)
    {
        Assert.Same(config, WebClientFunctions.AddPluginToConfig(config));
    }

    [Theory]
    [InlineData("{\"plugins\":\"htmlVideoPlayer/plugin\"}")]
    [InlineData("{\"plugins\":{\"0\":\"htmlVideoPlayer/plugin\"}}")]
    [InlineData("{\"plugins\":null}")]
    [InlineData("{\"plugins\":42}")]
    public void AddPluginToConfig_NonArrayPlugins_ReturnsInputUnchanged(string config)
    {
        Assert.Same(config, WebClientFunctions.AddPluginToConfig(config));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"plugins\":[")]
    [InlineData("{\"plugins\":[]} trailing")]
    [InlineData("{\"plugins\":[],}")]
    [InlineData("{\"plugins\":[] /* comment */}")]
    [InlineData("{\"plugins\":[],\"plugins\":[]}")]
    public void AddPluginToConfig_MalformedJSON_ReturnsInputUnchanged(string config)
    {
        Assert.Same(config, WebClientFunctions.AddPluginToConfig(config));
    }

    private static JsonObject ReadSettings(string scripts)
    {
        const string ArgumentStart = "})(";
        const string ArgumentEnd = ");</script>";
        int start = scripts.IndexOf(ArgumentStart, StringComparison.Ordinal) + ArgumentStart.Length;
        int end = scripts.LastIndexOf(ArgumentEnd, StringComparison.Ordinal);
        return JsonNode.Parse(scripts[start..end])!.AsObject();
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
