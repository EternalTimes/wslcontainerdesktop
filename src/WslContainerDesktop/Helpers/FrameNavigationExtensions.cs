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

using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WslContainerDesktop.Services;

namespace WslContainerDesktop.Helpers;

/// <summary>Navigation and visual-tree helpers that honor animation and indicator preferences.</summary>
public static class FrameNavigationExtensions
{
    // A list's own item transitions, kept while they are stripped so turning animations back on restores them.
    private static readonly DependencyProperty OriginalItemTransitionsProperty =
        DependencyProperty.RegisterAttached(
            "OriginalItemTransitions",
            typeof(TransitionCollection),
            typeof(FrameNavigationExtensions),
            new PropertyMetadata(null));

    // A saved local value also marks the indicator as hidden, so no second flag is needed.
    private static readonly DependencyProperty OriginalIndicatorVisibilityProperty =
        DependencyProperty.RegisterAttached(
            "OriginalIndicatorVisibility",
            typeof(Visibility),
            typeof(FrameNavigationExtensions),
            new PropertyMetadata(Visibility.Visible));

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
    /// Applies indicator visibility and completes its animations at the current selection.
    /// Waits for queued native completion callbacks only when an animation was stopped.
    /// Hosted pages are excluded at the <see cref="Frame"/> boundary.
    /// </summary>
    public static Task CompleteNavIndicatorAnimationsAsync(NavigationView navigation, bool visible = true)
    {
        if (!CompleteNavIndicatorAnimations(navigation, visible, selected: false))
        {
            return Task.CompletedTask;
        }

        var compositor = ElementCompositionPreview.GetElementVisual(navigation).Compositor;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var sentinel = compositor.CreateSpriteVisual();
        sentinel.Opacity = 0;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(1);

        void OnCompleted(object sender, CompositionBatchCompletedEventArgs args)
        {
            batch.Completed -= OnCompleted;
            sentinel.Dispose();
            animation.Dispose();
            batch.Dispose();
            // The compositor must process the stops before completing this later animation.
            // Let WinUI's queued completion handlers run before admitting another slide.
            if (!navigation.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => completion.TrySetResult()))
            {
                completion.TrySetResult();
            }
        }

        batch.Completed += OnCompleted;
        sentinel.StartAnimation("Opacity", animation);
        batch.End();
        return completion.Task;
    }

    private static bool CompleteNavIndicatorAnimations(DependencyObject root, bool visible, bool selected)
    {
        if (root is Frame)
        {
            return false;
        }

        if (root is NavigationViewItem item)
        {
            selected = item.IsSelected;
        }

        var stoppedAnimation = false;
        if (root is FrameworkElement { Name: "SelectionIndicator" } indicator)
        {
            var visual = ElementCompositionPreview.GetElementVisual(indicator);
            // NavigationView starts animations on component paths, so stop those exact paths.
            foreach (var path in IndicatorAnimationPaths)
            {
                stoppedAnimation |= visual.TryGetAnimationController(path) is not null;
                visual.StopAnimation(path);
            }
            visual.Offset = Vector3.Zero;
            visual.Scale = Vector3.One;
            visual.CenterPoint = Vector3.Zero;
            indicator.Opacity = selected ? 1 : 0;
            visual.Opacity = selected ? 1 : 0;

            // Settle before hiding or revealing the indicator so its animation is still observable
            // and restoring visibility never exposes a stale selection or an intermediate slide.
            var originalVisibility = indicator.ReadLocalValue(OriginalIndicatorVisibilityProperty);
            if (visible)
            {
                if (originalVisibility is Visibility original)
                {
                    indicator.Visibility = original;
                    indicator.ClearValue(OriginalIndicatorVisibilityProperty);
                }
            }
            else if (originalVisibility is not Visibility)
            {
                indicator.SetValue(OriginalIndicatorVisibilityProperty, indicator.Visibility);
                indicator.Visibility = Visibility.Collapsed;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            stoppedAnimation |= CompleteNavIndicatorAnimations(VisualTreeHelper.GetChild(root, i), visible, selected);
        }

        return stoppedAnimation;
    }

    private static readonly string[] IndicatorAnimationPaths =
        ["Opacity", "Offset.X", "Offset.Y", "Scale.X", "Scale.Y", "CenterPoint.X", "CenterPoint.Y"];

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
