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

using Xunit;

namespace WslContainerDesktop.Tests.Helpers;

public sealed class FrameNavigationExtensionsTests
{
    [Fact]
    public void IndicatorPreferenceMasksVisibilityWithoutChangingNavigationOwnedOpacity()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "WslContainerDesktop", "Helpers", "FrameNavigationExtensions.cs"));
        var method = ExtractMethod(source, "public static void SetNavIndicatorVisible");

        Assert.Contains("if (root is Frame)", method);
        Assert.True(method.IndexOf("if (root is Frame)", StringComparison.Ordinal)
            < method.IndexOf("root is FrameworkElement { Name: \"SelectionIndicator\" }", StringComparison.Ordinal));
        Assert.Contains("typeof(Visibility)", source);
        Assert.Contains("typeof(bool)", source);
        Assert.Contains("indicator.SetValue(OriginalIndicatorVisibilityProperty, indicator.Visibility)", method);
        Assert.Contains("indicator.Visibility = Visibility.Collapsed", method);
        Assert.Contains("indicator.Visibility = (Visibility)indicator.GetValue(OriginalIndicatorVisibilityProperty)", method);
        Assert.True(method.IndexOf("indicator.SetValue(OriginalIndicatorVisibilityProperty, indicator.Visibility)", StringComparison.Ordinal)
            < method.IndexOf("indicator.Visibility = Visibility.Collapsed", StringComparison.Ordinal));
        Assert.Contains("if (!(bool)indicator.GetValue(IsIndicatorVisibilityMaskedProperty))", method);
        Assert.DoesNotContain(".Opacity", method);
        Assert.Contains("SetNavIndicatorVisible(VisualTreeHelper.GetChild(root, i), visible)", method);
    }

    private static string FindRepositoryFile(params string[] path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WslContainerDesktop.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return Path.Combine([root.FullName, .. path]);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method {signature}.");
        var openingBrace = source.IndexOf('{', start);
        Assert.True(openingBrace >= 0, $"Could not find body for method {signature}.");

        var depth = 0;
        for (var i = openingBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                return source[openingBrace..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Method {signature} has an unterminated body.");
    }
}
