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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WslContainerDesktop.Services;

namespace WslContainerDesktop.Helpers;

/// <summary>Navigation and visual-tree helpers that honor the "Animations" setting.</summary>
public static class FrameNavigationExtensions
{
    // A list's own item transitions, kept while they are stripped so turning animations back on restores them.
    private static readonly DependencyProperty OriginalItemTransitionsProperty =
        DependencyProperty.RegisterAttached(
            "OriginalItemTransitions",
            typeof(TransitionCollection),
            typeof(FrameNavigationExtensions),
            new PropertyMetadata(null));

    // Until something has been stripped there is nothing to restore, so an animations-on walk can be skipped.
    private static bool s_anyStripped;

    /// <summary>Whether the user has animations turned on (the default).</summary>
    public static bool AnimationsEnabled =>
        App.Current.Services.GetService<ISettingsService>()?.PageAnimations ?? true;

    /// <summary>Navigates to a page, skipping the slide transition and list animations when animations are off.</summary>
    public static bool NavigateWithPreference(this Frame frame, Type pageType, object? parameter = null)
    {
        var navigated = AnimationsEnabled
            ? frame.Navigate(pageType, parameter)
            : frame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());

        if (navigated && frame.Content is FrameworkElement page && (!AnimationsEnabled || s_anyStripped))
        {
            // Lists exist once the page is built; templated ones appear when it loads. The handler
            // removes itself so a cached page that is visited repeatedly never accumulates handlers.
            ApplyItemTransitions(page, AnimationsEnabled);
            RoutedEventHandler? onLoaded = null;
            onLoaded = (_, _) =>
            {
                page.Loaded -= onLoaded;
                ApplyItemTransitions(page, AnimationsEnabled);
            };
            page.Loaded += onLoaded;
        }

        return navigated;
    }

    /// <summary>
    /// Shows or hides the navigation pane's sliding selection indicator. The selected item keeps
    /// its highlight; only the bar that animates between items is hidden. The walk stops at any
    /// <see cref="Frame"/>, so the hosted page is neither scanned nor affected.
    /// </summary>
    public static void SetNavIndicatorVisible(DependencyObject root, bool visible)
    {
        if (root is Frame)
        {
            return;
        }

        if (root is FrameworkElement { Name: "SelectionIndicator" } indicator)
        {
            indicator.Opacity = visible ? 1 : 0;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            SetNavIndicatorVisible(VisualTreeHelper.GetChild(root, i), visible);
        }
    }

    /// <summary>
    /// Removes (or, when <paramref name="animate"/> is true, restores) the item entrance/add/reorder
    /// transitions every ListView and GridView plays by default — the "whole list slides up" effect.
    /// </summary>
    public static void ApplyItemTransitions(DependencyObject root, bool animate)
    {
        if (animate && !s_anyStripped)
        {
            return;
        }

        if (root is ListViewBase list)
        {
            var original = list.GetValue(OriginalItemTransitionsProperty) as TransitionCollection;
            if (animate)
            {
                if (original is not null)
                {
                    list.ItemContainerTransitions = original;
                    list.ClearValue(OriginalItemTransitionsProperty);
                }
            }
            else if (original is null && list.ItemContainerTransitions is { Count: > 0 } current)
            {
                list.SetValue(OriginalItemTransitionsProperty, current);
                list.ItemContainerTransitions = new TransitionCollection();
                s_anyStripped = true;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            ApplyItemTransitions(VisualTreeHelper.GetChild(root, i), animate);
        }
    }
}
