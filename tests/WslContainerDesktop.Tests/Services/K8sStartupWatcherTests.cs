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

using WslContainerDesktop.Models;
using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

public sealed class K8sStartupWatcherTests
{
    [Fact]
    public async Task CanceledProbeDoesNotBlockReplacementOrClearItOnCompletion()
    {
        var responses = new Queue<TaskCompletionSource<ClusterStatus>>();
        var oldResponse = new TaskCompletionSource<ClusterStatus>();
        var newResponse = new TaskCompletionSource<ClusterStatus>();
        responses.Enqueue(oldResponse);
        responses.Enqueue(newResponse);
        var applied = new List<ClusterStatus>();
        var calls = 0;
        var ready = 0;
        var watcher = new K8sStartupWatcher(Visible(), _ =>
        {
            calls++;
            return responses.Dequeue().Task;
        }, applied.Add, () => ready++, (_, _) => Task.CompletedTask);

        var old = watcher.StartAsync();
        watcher.Stop();
        var current = watcher.StartAsync();
        Assert.Equal(2, calls);
        oldResponse.SetResult(new() { State = ClusterState.Running });
        await old;
        await watcher.StartAsync();
        Assert.Equal(2, calls);
        Assert.Empty(applied);
        Assert.Equal(0, ready);

        newResponse.SetResult(new() { State = ClusterState.Running });
        await current;
        Assert.Single(applied);
        Assert.Equal(1, ready);
    }

    [Fact]
    public async Task HiddenDuringDelayDoesNotIssueProbe()
    {
        var visibility = Visible();
        var calls = 0;
        var watcher = new K8sStartupWatcher(visibility, _ =>
        {
            calls++;
            return Task.FromResult(new ClusterStatus { State = ClusterState.Running });
        }, _ => { }, () => { }, (_, _) =>
        {
            visibility.SetDisplayOn(false);
            return Task.CompletedTask;
        });
        var work = watcher.StartAsync();
        Assert.Equal(0, calls);
        watcher.Stop();
        await work;
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task BecomingVisibleAgainResumesTheSameWatcher()
    {
        var visibility = Visible();
        var calls = 0;
        var watcher = new K8sStartupWatcher(visibility, _ =>
        {
            calls++;
            return Task.FromResult(new ClusterStatus { State = ClusterState.Running });
        }, _ => { }, () => { }, (_, _) =>
        {
            visibility.SetDisplayOn(false);
            return Task.CompletedTask;
        });
        var work = watcher.StartAsync();
        Assert.Equal(0, calls);
        visibility.SetDisplayOn(true);
        await work;
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task LossStatusEndsWatcherWithoutStartingResourcePolling()
    {
        var calls = 0;
        var ready = 0;
        var applied = new List<ClusterStatus>();
        var watcher = new K8sStartupWatcher(Visible(), _ =>
        {
            calls++;
            return Task.FromResult(new ClusterStatus { State = ClusterState.Stopped });
        }, applied.Add, () => ready++, (_, _) => Task.CompletedTask);
        await watcher.StartAsync();
        Assert.Equal(1, calls);
        Assert.Single(applied);
        Assert.Equal(0, ready);
    }

    [Fact]
    public async Task BackoffRemainsTwoFourEightTenSeconds()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;
        var ready = 0;
        var watcher = new K8sStartupWatcher(Visible(), _ =>
        {
            calls++;
            return Task.FromResult(new ClusterStatus { State = calls < 5 ? ClusterState.Starting : ClusterState.Running });
        }, _ => { }, () => ready++, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        await watcher.StartAsync();
        Assert.Equal(new[] { 2, 4, 8, 10, 10 }, delays.Select(d => (int)d.TotalSeconds));
        Assert.Equal(1, ready);
    }

    [Fact]
    public async Task UnexpectedFailurePropagatesToUiSafeAndAllowsRetry()
    {
        var calls = 0;
        var watcher = new K8sStartupWatcher(Visible(), _ =>
        {
            calls++;
            throw new InvalidOperationException("fixture probe failure");
        }, _ => { }, () => { }, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(watcher.StartAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(watcher.StartAsync);
        Assert.Equal(2, calls);
    }

    private static WindowVisibility Visible()
    {
        var visibility = new WindowVisibility();
        visibility.SetWindowState(shown: true, minimized: false);
        return visibility;
    }
}
