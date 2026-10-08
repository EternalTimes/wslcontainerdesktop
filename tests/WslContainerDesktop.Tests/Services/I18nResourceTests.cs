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
