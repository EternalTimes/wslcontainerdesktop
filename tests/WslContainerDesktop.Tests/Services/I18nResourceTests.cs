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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Xml.Linq;
using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

public sealed class I18nResourceTests
{
    [Fact]
    public void SettingsUidsDoNotApplyPropertiesToDifferentControlTypes()
    {
        var source = SourceDirectory();
        var page = XDocument.Load(Path.Combine(source, "Views", "SettingsPage.xaml"));
        var uid = XName.Get("Uid", "http://schemas.microsoft.com/winfx/2006/xaml");
        var controls = page.Descendants()
            .Where(element => element.Attribute(uid) is not null)
            .GroupBy(element => element.Attribute(uid)!.Value);

        foreach (var group in controls)
        {
            Assert.True(group.Select(element => element.Name.LocalName).Distinct().Count() == 1,
                $"x:Uid '{group.Key}' is shared by different control types. Give each type its own resource key so WinUI never applies an unsupported property.");
        }

        Assert.All(page.Descendants().Where(element => element.Name.LocalName == "ToggleSwitch"),
            element => Assert.NotNull(element.Attribute(uid)));
    }

    [Fact]
    public void EnglishAndChineseResourceKeysStayInSync()
    {
        var strings = Path.Combine(SourceDirectory(), "Strings");
        var english = ResourceKeys(Path.Combine(strings, "en-US", "Resources.resw"));
        var chinese = ResourceKeys(Path.Combine(strings, "zh-Hans", "Resources.resw"));

        Assert.Equal(english, chinese);
    }

    /// <summary>
    /// Guards the crash where "follow the system" was written into a WinRT language property as an
    /// empty string. Both FrameworkElement.Language and ApplicationLanguages.PrimaryLanguageOverride
    /// reject it as a malformed BCP-47 tag and throw, ending the launch. An empty tag must stay
    /// empty through normalization so callers can branch on it instead of forwarding it.
    /// </summary>
    [Fact]
    public void FollowingTheSystemLanguageNormalizesToEmptyRatherThanAMalformedTag()
    {
        Assert.Equal(string.Empty, AppLanguage.Normalize(null));
        Assert.Equal(string.Empty, AppLanguage.Normalize(string.Empty));
        Assert.Equal(string.Empty, AppLanguage.Normalize("   "));

        // An unrecognized tag also falls back to the system rather than reaching WinUI unvalidated.
        Assert.Equal(string.Empty, AppLanguage.Normalize("not a tag"));
        Assert.Equal(string.Empty, AppLanguage.Normalize("fr-FR"));

        // Real tags survive untouched.
        Assert.Equal("zh-Hans", AppLanguage.Normalize("zh-Hans"));
        Assert.Equal("en-US", AppLanguage.Normalize("EN-US"));
    }

    /// <summary>
    /// Covers the stand-in used when the language override has to stand in for an unset one. MRT
    /// refuses an empty string on its setter and will not clear an override once written, so a live
    /// switch back to "follow the system" writes the language MRT had ranked first instead. Matching
    /// has to land on the same resources the unset override would have picked, which means the script
    /// subtag decides: a traditional Chinese language must not land on the simplified resources.
    /// </summary>
    [Theory]
    [InlineData("en-US", "en-US")]        // exact
    [InlineData("EN-us", "en-US")]        // case-insensitive
    [InlineData("en-GB", "en-US")]        // same language, no script claimed either side
    [InlineData("en", "en-US")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("zh-CN", "zh-Hans")]      // simplified Chinese script
    [InlineData("zh-SG", "zh-Hans")]      // same, different region
    [InlineData("zh-TW", "")]             // traditional script must not pick simplified resources
    [InlineData("zh-Hant", "")]
    [InlineData("zh", "")]                // ambiguous script: decline rather than guess
    [InlineData("de-DE", "")]             // unsupported: let the caller leave the override alone
    [InlineData("fr", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void SystemLanguagesMapOntoTheShippedResource(string? languageTag, string expected)
    {
        Assert.Equal(expected, AppLanguage.MatchSupported(languageTag));
    }

    /// <summary>
    /// <see cref="AppLanguage.MatchSupported"/> skips the "follow the system" placeholder by testing
    /// for an empty tag, so the placeholder has to stay first and stay empty. Reordering the list
    /// would make it match profile languages as if it were a real language.
    /// </summary>
    [Fact]
    public void TheSystemPlaceholderStaysFirstAndEmpty()
    {
        Assert.Equal(AppLanguage.SystemDefault, AppLanguage.Supported[0].Tag);
        Assert.Equal(string.Empty, AppLanguage.Supported[0].Tag);

        // Every other entry is a real BCP-47 tag that could be written to the MRT override.
        foreach (var option in AppLanguage.Supported.Skip(1))
        {
            Assert.NotEqual(AppLanguage.SystemDefault, option.Tag);
            Assert.Equal(option.Tag, AppLanguage.Normalize(option.Tag));
        }
    }

    private static string[] ResourceKeys(string path) => XDocument.Load(path).Root!
        .Elements("data")
        .Select(element => (string)element.Attribute("name")!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WslContainerDesktop.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "WslContainerDesktop");
    }
}
