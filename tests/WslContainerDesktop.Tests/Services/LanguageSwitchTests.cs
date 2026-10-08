// WSL Container Desktop - a WinUI 3 manager for WSL containers.
// Copyright (C) 2026 Michael Hacker
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

public sealed class LanguageSwitchTests
{
    [Theory]
    [InlineData(null, "zh-CN", "zh-Hans")]
    [InlineData("", "zh-CN", "zh-Hans")]
    [InlineData("   ", "en-GB", "en-US")]
    [InlineData("invalid", "zh-SG", "zh-Hans")]
    [InlineData("", "de-DE", "en-US")]
    [InlineData("", "zh-Hant", "en-US")]
    [InlineData("en-US", "zh-CN", "en-US")]
    [InlineData("zh-Hans", "en-US", "zh-Hans")]
    public void NativeWriterOnlyReceivesEffectiveSupportedTags(string? preference, string system, string expected)
    {
        string? written = null;
        var effective = AppLanguage.Apply(preference, [system], tag =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tag));
            Assert.Equal(tag, AppLanguage.Normalize(tag));
            written = tag;
        });
        Assert.Equal(expected, effective);
        Assert.Equal(expected, written);
    }

    [Fact]
    public void ReturningToSystemAndRelaunchingIgnorePreviousApplicationOverride()
    {
        var written = new List<string>();
        AppLanguage.Apply("en-US", ["zh-CN"], written.Add);
        AppLanguage.Apply(AppLanguage.SystemDefault, ["zh-CN"], written.Add);
        AppLanguage.Apply(AppLanguage.SystemDefault, ["zh-CN"], written.Add);
        Assert.Equal(new[] { "en-US", "zh-Hans", "zh-Hans" }, written);
        Assert.Equal("", AppLanguage.SystemDefault);
    }

    [Fact]
    public void UnsupportedOrEmptySystemListsHaveAnExplicitNonemptyFallback()
    {
        Assert.Equal("en-US", AppLanguage.Resolve("", []));
        Assert.Equal("zh-Hans", AppLanguage.Resolve("", ["fr-FR", "zh-CN", "en-US"]));
    }
}

[CollectionDefinition("UiText", DisableParallelization = true)]
public sealed class UiTextCollection;

[Collection("UiText")]
public sealed class DisplayTranslationTests : IDisposable
{
    private readonly Dictionary<string, string> _resources = new()
    {
        ["Test_Progress"] = "已下载 {0}%，镜像 {1}。",
        ["Test_Label"] = "刷新",
        ["Test_Repeated"] = "{0} 已结束；{0} 已停止。",
    };

    public DisplayTranslationTests() => UiText.Configure((key, args) => string.Format(_resources[key], args),
    [
        ("Test_Progress", "{0}% downloaded for image {1}.", "已下载 {0}%，镜像 {1}。"),
        ("Test_Label", "Refresh", "刷新"),
        ("Test_Repeated", "{0} finished; {0} stopped.", "{0} 已结束；{0} 已停止。"),
    ]);

    [Fact]
    public void DisplayProjectionPreservesParametersAndCanSwitchBack()
    {
        var original = "42% downloaded for image ghcr.io/example/model:latest.";
        var translated = UiText.Translate(original);
        Assert.Equal("已下载 42%，镜像 ghcr.io/example/model:latest。", translated);
        _resources["Test_Progress"] = "{0}% downloaded for image {1}.";
        Assert.Equal(original, UiText.Translate(translated));
    }

    [Fact]
    public void UnknownExternalTextIsPreservedAndRepeatedArgumentsMustAgree()
    {
        Assert.Equal("stderr: image not found", UiText.Translate("stderr: image not found"));
        Assert.Equal("one finished; two stopped.", UiText.Translate("one finished; two stopped."));
        Assert.Equal("one 已结束；one 已停止。", UiText.Translate("one finished; one stopped."));
        Assert.Equal("刷新", UiText.Translate("Refresh"));
    }

    public void Dispose() => UiText.Configure(null);
}
