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

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

/// <summary>Verifies the independent indicator preference survives upgrades and settings notifications.</summary>
public sealed class NavigationIndicatorSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "navigation-indicator-" + Guid.NewGuid().ToString("N"));
    private string SettingsFile => Path.Combine(_directory, "settings.json");
    private SettingsService CreateSettings() => new(NullLogger<SettingsService>.Instance, _directory);

    [Fact]
    public void NewSettingsShowIndicatorByDefault()
    {
        var settings = CreateSettings();
        settings.Load();

        Assert.True(settings.NavigationIndicatorEnabled);
        Assert.True(settings.PageAnimations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSettingsWithoutIndicatorPreferenceKeepItVisible(bool animations)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new { PageAnimations = animations, WslUpdateChannelGaMigrated = true }));
        var settings = CreateSettings();

        settings.Load();

        Assert.Equal(animations, settings.PageAnimations);
        Assert.True(settings.NavigationIndicatorEnabled);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AllAnimationAndIndicatorCombinationsRoundTripIndependently(bool animations, bool indicator)
    {
        var original = CreateSettings();
        original.PageAnimations = animations;
        original.NavigationIndicatorEnabled = indicator;

        original.Save();
        using var json = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        Assert.Equal(animations, json.RootElement.GetProperty(nameof(ISettingsService.PageAnimations)).GetBoolean());
        Assert.Equal(indicator, json.RootElement.GetProperty(nameof(ISettingsService.NavigationIndicatorEnabled)).GetBoolean());
        var restored = CreateSettings();
        restored.Load();

        Assert.Equal(animations, restored.PageAnimations);
        Assert.Equal(indicator, restored.NavigationIndicatorEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavingIndicatorChangeNotifiesObserversWithPersistedIndependentState(bool indicator)
    {
        var settings = CreateSettings();
        settings.PageAnimations = !indicator;
        settings.NavigationIndicatorEnabled = !indicator;
        var changes = 0;
        settings.Changed += (sender, args) =>
        {
            changes++;
            Assert.Same(settings, sender);
            Assert.Same(EventArgs.Empty, args);
            Assert.Equal(indicator, settings.NavigationIndicatorEnabled);
            Assert.Equal(!indicator, settings.PageAnimations);
            using var json = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            Assert.Equal(indicator, json.RootElement.GetProperty(nameof(ISettingsService.NavigationIndicatorEnabled)).GetBoolean());
        };

        settings.NavigationIndicatorEnabled = indicator;
        settings.Save();

        Assert.Equal(1, changes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
