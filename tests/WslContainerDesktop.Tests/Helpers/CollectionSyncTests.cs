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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WslContainerDesktop.Helpers;
using Xunit;

namespace WslContainerDesktop.Tests.Helpers;

/// <summary>
/// Covers the in-place list update used by inventory pages: it must end up equal to the new items
/// without ever raising a Reset, which is what scrolls a bound list back to the top.
/// </summary>
public sealed class CollectionSyncTests
{
    private sealed record Row(string Name);

    private static (ObservableCollection<Row> Target, List<NotifyCollectionChangedAction> Actions) Track(params Row[] initial)
    {
        var target = new ObservableCollection<Row>(initial);
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);
        return (target, actions);
    }

    [Fact]
    public void ReplaceAll_FromEmpty_AddsEveryItemInOrder()
    {
        var (target, actions) = Track();
        Row[] items = [new("a"), new("b"), new("c")];

        CollectionSync.ReplaceAll(target, items);

        Assert.Equal(items, target);
        Assert.All(actions, a => Assert.Equal(NotifyCollectionChangedAction.Add, a));
    }

    [Fact]
    public void ReplaceAll_WithSameInstances_RaisesNothing()
    {
        Row a = new("a"), b = new("b");
        var (target, actions) = Track(a, b);

        CollectionSync.ReplaceAll(target, [a, b]);

        Assert.Equal([a, b], target);
        Assert.Empty(actions);
    }

    [Fact]
    public void ReplaceAll_WhenShrinking_TrimsTheTail()
    {
        Row a = new("a");
        var (target, actions) = Track(a, new("b"), new("c"));

        CollectionSync.ReplaceAll(target, [a]);

        Assert.Equal([a], target);
        Assert.Equal([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove], actions);
    }

    [Fact]
    public void ReplaceAll_WhenGrowingWithChangedRows_ReplacesThenAdds()
    {
        Row a = new("a");
        var (target, actions) = Track(a, new("old"));
        Row[] items = [a, new("new"), new("extra")];

        CollectionSync.ReplaceAll(target, items);

        Assert.Equal(items, target);
        Assert.Equal([NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Add], actions);
    }

    [Fact]
    public void ReplaceAll_ToEmpty_ClearsWithoutReset()
    {
        var (target, actions) = Track(new("a"), new("b"));

        CollectionSync.ReplaceAll(target, []);

        Assert.Empty(target);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }
}
