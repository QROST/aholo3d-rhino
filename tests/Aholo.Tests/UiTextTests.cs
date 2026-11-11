using System.Globalization;
using System.Text.RegularExpressions;
using Aholo.Core;
namespace Aholo.Tests;

[Collection("UI language state")]
public sealed class UiTextTests
{
    [Theory]
    [InlineData(1033, "zh-CN", "en")]
    [InlineData(1036, "zh-TW", "en")]
    [InlineData(2052, "en-US", "zh")]
    [InlineData(1028, "en-US", "zh")]
    [InlineData(3076, "en-US", "zh")]
    [InlineData(null, "zh-Hans", "zh")]
    [InlineData(null, "zh-Hant", "zh")]
    [InlineData(0, "zh-CN", "zh")]
    [InlineData(127, "zh-TW", "zh")]
    [InlineData(-1, "zh-HK", "zh")]
    [InlineData(999999, "zh-CN", "zh")]
    [InlineData(null, "fr-FR", "en")]
    [InlineData(null, "", "en")]
    [InlineData(null, null, "en")]
    public void RhinoLanguageWinsAndSystemIsOnlyFallback(int? rhino, string? system, string expected) =>
        Assert.Equal(expected, UiText.SelectLanguage(rhino, system));

    [Fact]
    public void EmbeddedCatalogHasEnglishSourceAndPreservesEveryFormatPlaceholder()
    {
        Assert.True(UiText.Catalog.Count >= 377);
        var placeholders = new Regex(@"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}");
        foreach (var entry in UiText.Catalog)
        {
            Assert.DoesNotMatch(@"\p{IsCJKUnifiedIdeographs}", entry.English);
            Assert.NotEmpty(entry.Chinese);
            Assert.Equal(placeholders.Matches(entry.English).Select(m => m.Value).OrderBy(s => s),
                placeholders.Matches(entry.Chinese).Select(m => m.Value).OrderBy(s => s));
            Assert.Equal(entry.English, UiText.Get(entry.English, "en"));
        }
        foreach (var entry in UiText.Catalog.DistinctBy(e => e.English))
            Assert.Equal(entry.Chinese, UiText.Get(entry.English, "zh"));
    }

    [Fact]
    public void SharedChineseLabelsDoNotBreakInitializationOrGuessReverseMeaning()
    {
        Assert.Equal("云资产", UiText.Get("Cloud asset", "zh"));
        Assert.Equal("云资产", UiText.Get("Cloud assets", "zh"));
        Assert.Equal("Cloud assets", UiText.Get("Cloud assets", "en"));
        Assert.Equal("云资产", UiText.TranslateKnownMessage("云资产", "en"));
    }

    [Fact]
    public async Task BackgroundMessagesFollowHostLanguageWithoutChangingFormattingCulture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            UiText.Initialize(2052, "en-US");
            Assert.Equal("就绪", await Task.Run(() => UiText.Get("Ready")));
            Assert.Contains("1", UiText.Format("Preparing complete GS assets {0:N0} / {1:N0}…", 1, 2));
            UiText.Initialize(1033, "zh-CN");
            Assert.Equal("Ready", await Task.Run(() => UiText.Get("Ready")));
            Assert.Same(culture, CultureInfo.CurrentCulture); Assert.Same(uiCulture, CultureInfo.CurrentUICulture);
        }
        finally { UiText.Initialize(1033, null); }
    }

    [Fact]
    public void KnownErrorsTranslateInBothDirectionsAndPreserveCapturedParameters()
    {
        Assert.Equal("国际区域 API Key 无效或已失效。", UiText.TranslateKnownMessage("The international API Key is invalid or expired.", "zh"));
        Assert.Equal("Service request failed (HTTP 503).", UiText.TranslateKnownMessage("服务请求失败 (HTTP 503)。", "en"));
        const string path = "/tmp/用户 model/{0}/world.usdz";
        string english = $"Exported 2 GS blocks and 13 Gaussians.\n{path}\n";
        string chinese = UiText.TranslateKnownMessage(english, "zh");
        Assert.Equal($"已导出 2 个 GS 块、13 个高斯。\n{path}\n", chinese);
        Assert.Equal(english, UiText.TranslateKnownMessage(chinese, "en"));
        const string external = "用户 prompt Service request failed (HTTP 503).";
        Assert.Equal(external, UiText.TranslateKnownMessage(external, "zh"));
        Assert.Equal("new unknown message", UiText.Get("new unknown message", "zh"));
    }

    [Fact]
    public void LongUnmatchedErrorsNeverThrowOrLoseOriginalMessage()
    {
        string message = "Exported " + new string('x', 32000) + " GS blocks and no matching suffix";
        Assert.Equal(message, UiText.TranslateKnownMessage(message, "zh"));
        message += new string('x', 40000);
        Assert.Equal(message, UiText.TranslateKnownMessage(message, "zh"));
    }

    [Fact]
    public void StatusLabelsDoNotChangePersistedCodesOrUserNames()
    {
        const string name = "用户原文 Service request failed";
        var task = new LocalTask { Name = name, Status = "RUNNING" };
        try
        {
            UiText.Initialize(2052, null);
            Assert.Equal(name + " · 进行中", task.ToString());
            Assert.Equal("RUNNING", task.Status); Assert.Equal(name, task.Name);
            Assert.Equal("unknown-status", UiText.Status("unknown-status", "zh"));
        }
        finally { UiText.Initialize(1033, null); }
    }
}
[CollectionDefinition("UI language state", DisableParallelization = true)]
public sealed class UiLanguageCollection { }
