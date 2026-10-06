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

using Microsoft.Extensions.Logging.Abstractions;
using WslContainerDesktop.Models;
using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

public sealed class K8sClusterLifecycleTests
{
    [Fact]
    public async Task FooterNeverWaitsForLongLifecycleWork()
    {
        var f = new Fixture();
        var completion = new TaskCompletionSource<bool>();
        var install = f.Change(K8sLifecycleAction.Install, _ => completion.Task);
        try
        {
            var footer = f.Lifecycle.GetFooterStatusAsync(default);
            Assert.True(footer.IsCompleted);
            Assert.Equal(ClusterState.Working, (await footer).State);
            Assert.Equal(0, f.Probe.FooterCalls);
        }
        finally
        {
            completion.SetResult(true);
            await install;
        }
    }

    [Fact]
    public async Task OlderRunningResponseCannotUndoStop()
    {
        var f = new Fixture();
        var response = new TaskCompletionSource<ClusterStatus>();
        f.Probe.Status = _ => response.Task;
        var read = f.Read();
        Assert.Equal(1, f.Probe.StatusCalls);

        var stop = f.Change(K8sLifecycleAction.Stop);
        Assert.False(stop.IsCompleted);
        response.SetResult(Running());
        Assert.Equal(ClusterState.Unknown, (await read).State);
        Assert.True(await stop);
        Assert.True(f.Stopped);
        Assert.True(f.SavedStopped);
        Assert.Null(f.Hold.Session);
        Assert.Equal(0, f.Hold.Starts);
    }

    [Fact]
    public async Task ReadAndSyncQueuedDuringStopCannotRestartIt()
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        var completeStop = new TaskCompletionSource<bool>();
        var stop = f.Change(K8sLifecycleAction.Stop, _ => completeStop.Task);
        var read = f.Read();
        var sync = f.Lifecycle.SyncKeepAliveAsync(default);
        Assert.True(f.Stopped);
        completeStop.SetResult(true);

        await stop;
        Assert.Equal(ClusterState.Unknown, (await read).State);
        await sync;
        Assert.Equal(0, f.Probe.StatusCalls);
        Assert.Equal(1, f.Hold.Starts);
        Assert.Null(f.Hold.Session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledStopKeepsIntentAndReleasesSession(bool cancel)
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                f.Change(K8sLifecycleAction.Stop, _ => Task.FromCanceled<bool>(new CancellationToken(true))));
        }
        else
        {
            Assert.False(await f.Change(K8sLifecycleAction.Stop, _ => Task.FromResult(false)));
        }

        Assert.True(f.Stopped);
        Assert.True(f.SavedStopped);
        Assert.Null(f.Hold.Session);
        await f.Lifecycle.SyncKeepAliveAsync(default);
        Assert.Equal(1, f.Hold.Starts);
    }

    [Fact]
    public async Task ExplicitStartAfterStopReacquiresOneSession()
    {
        var f = new Fixture();
        await f.Change(K8sLifecycleAction.Stop);
        await f.Change(K8sLifecycleAction.Start);
        Assert.False(f.Stopped);
        Assert.NotNull(f.Hold.Session);
        Assert.Equal(1, f.Hold.Starts);
    }

    [Fact]
    public async Task FreshDiscoveryRecognizesAnExternalRestart()
    {
        var f = new Fixture();
        await f.Change(K8sLifecycleAction.Stop);
        Assert.Equal(ClusterState.Running, (await f.Read()).State);
        Assert.False(f.Stopped);
        Assert.False(f.SavedStopped);
        Assert.Equal(1, f.Hold.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoppedDistroAndUnknownInventoryDoNotBoot(bool unknown)
    {
        var f = new Fixture { Stopped = true, Running = new HashSet<string>() };
        if (unknown)
        {
            f.Host = null;
        }

        var result = await f.Read();
        Assert.Equal(unknown ? ClusterState.Unknown : ClusterState.Stopped, result.State);
        Assert.Equal(0, f.Probe.StatusCalls);
        Assert.Equal(0, f.Hold.Starts);
    }

    [Fact]
    public async Task RepeatedObservationsAfterLossLaunchNoDistroCommands()
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        f.Hold.Release();
        var listCalls = f.ListCalls;
        for (var i = 0; i < 100; i++)
        {
            var status = await f.Read(observe: true);
            Assert.Equal(ClusterState.Stopped, status.State);
            Assert.Contains("automatic checks are paused", status.Message);
            Assert.True((await f.Lifecycle.GetFooterStatusAsync(default)).NotKeptRunning);
        }

        Assert.Equal(0, f.Probe.StatusCalls);
        Assert.Equal(0, f.Probe.FooterCalls);
        Assert.Equal(listCalls, f.ListCalls);
        Assert.Equal(1, f.Hold.Starts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostOrReplacedHoldInvalidatesInFlightStartupResult(bool replace)
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        var response = new TaskCompletionSource<ClusterStatus>();
        f.Probe.Status = _ => response.Task;
        var read = f.Read(observe: true);
        f.Hold.Release();
        if (replace)
        {
            f.Hold.Hold("Ubuntu");
        }

        response.SetResult(Running());
        Assert.Equal(ClusterState.Stopped, (await read).State);
        Assert.Equal(replace ? 2 : 1, f.Hold.Starts);
    }

    [Fact]
    public async Task OldFooterCannotReleaseAReplacementSession()
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        var response = new TaskCompletionSource<K8sFooterStatus>();
        f.Probe.Footer = (_, _) => response.Task;
        var read = f.Lifecycle.GetFooterStatusAsync(default);
        f.Hold.Release();
        f.Hold.Hold("Ubuntu");
        var replacement = f.Hold.Session;
        response.SetResult(new K8sFooterStatus { State = ClusterState.NotInstalled, Distro = "Ubuntu" });
        Assert.Equal(ClusterState.Unknown, (await read).State);
        Assert.Same(replacement, f.Hold.Session);
    }

    [Fact]
    public async Task CanceledStatusReleasesGateWithoutReconcilingIntent()
    {
        var f = new Fixture { Stopped = true };
        f.Probe.Status = _ => Task.FromCanceled<ClusterStatus>(new CancellationToken(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Read());
        Assert.True(f.Stopped);
        Assert.Equal(0, f.Hold.Starts);
        Assert.True(await f.Change(K8sLifecycleAction.Stop));
    }

    [Fact]
    public async Task HoldLostDuringRunningListPreventsProbe()
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        f.BeforeList = () => f.Hold.Release();
        Assert.Equal(ClusterState.Stopped, (await f.Read(observe: true)).State);
        Assert.Equal(0, f.Probe.StatusCalls);
        Assert.Equal(1, f.Hold.Starts);
    }

    [Fact]
    public async Task ShutdownInvalidatesInFlightDiscoveryAndQueuedSync()
    {
        var f = new Fixture();
        var response = new TaskCompletionSource<ClusterStatus>();
        f.Probe.Status = _ => response.Task;
        var read = f.Read();
        var sync = f.Lifecycle.SyncKeepAliveAsync(default);
        f.Lifecycle.Shutdown();
        response.SetResult(Running());
        Assert.Equal(ClusterState.Unknown, (await read).State);
        await sync;
        await f.Lifecycle.SyncKeepAliveAsync(default);
        Assert.Equal(0, f.Hold.Starts);
        Assert.False(f.Lifecycle.CanObserve);
    }

    [Fact]
    public async Task HostSelectionInvalidatesOlderDiscoveryAndReleasesOldHold()
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        var response = new TaskCompletionSource<ClusterStatus>();
        f.Probe.Status = _ => response.Task;
        var read = f.Read();
        var changeHost = f.Lifecycle.UseDefaultDistributionAsync(default);
        response.SetResult(Running());
        Assert.Equal(ClusterState.Unknown, (await read).State);
        await changeHost;
        Assert.Null(f.Pinned);
        Assert.Null(f.Hold.Session);
        Assert.Equal(1, f.Hold.Starts);
    }

    [Fact]
    public async Task ObservationDoesNotReconcileStopOrAcquireHold()
    {
        var f = new Fixture { Stopped = true };
        f.Hold.Hold("Ubuntu");
        Assert.Equal(ClusterState.Stopped, (await f.Read(observe: true)).State);
        Assert.True(f.Stopped);
        Assert.Equal(0, f.Probe.StatusCalls);
        Assert.Equal(1, f.Hold.Starts);
    }

    [Fact]
    public async Task AppLaunchAndDiscoveryKeepExactlyOneIdleSession()
    {
        var f = new Fixture();
        await f.Lifecycle.SyncKeepAliveAsync(default);
        await f.Read();
        await f.Read(observe: true);
        await f.Lifecycle.GetFooterStatusAsync(default);
        Assert.Equal(1, f.Hold.Starts);
        Assert.Equal(1, f.Probe.StateCalls);
    }

    [Fact]
    public async Task NoPinLaunchStartsNothingAndDiscoveryPinsOnlyInstalledCluster()
    {
        var f = new Fixture { Pinned = null };
        await f.Lifecycle.SyncKeepAliveAsync(default);
        Assert.Equal(0, f.Hold.Starts);
        await f.Read();
        Assert.Equal("Ubuntu", f.Pinned);
        Assert.Equal(1, f.Hold.Starts);
    }

    private static ClusterStatus Running() => new() { State = ClusterState.Running, Distro = "Ubuntu" };

    private sealed class Fixture
    {
        public bool Stopped;
        public bool SavedStopped;
        public string? Pinned = "Ubuntu";
        public KubernetesHost? Host = new(KubernetesHostProblem.None, "Ubuntu");
        public IReadOnlySet<string>? Running = new HashSet<string> { "Ubuntu" };
        public int ListCalls;
        public Action? BeforeList;
        public readonly HoldFake Hold = new();
        public readonly ProbeFake Probe = new();
        public K8sClusterLifecycle Lifecycle { get; }

        public Fixture()
        {
            var settings = NetworkTestProxy.Create<ISettingsService>((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_WslDistro": return Pinned;
                    case "set_WslDistro": Pinned = (string?)args[0]; return null;
                    case "get_KubernetesStoppedByUser": return Stopped;
                    case "set_KubernetesStoppedByUser": Stopped = (bool)args[0]!; return null;
                    case "Save": SavedStopped = Stopped; return null;
                    default: throw new InvalidOperationException(method.Name);
                }
            });
            Lifecycle = new K8sClusterLifecycle(settings, () => Host, _ =>
            {
                ListCalls++;
                BeforeList?.Invoke();
                return Task.FromResult<IReadOnlySet<string>?>(Running);
            }, Probe, Hold, NullLogger<K8sClusterLifecycle>.Instance);
        }

        public Task<ClusterStatus> Read(bool observe = false) => Lifecycle.GetStatusAsync(observe, default);
        public Task<bool> Change(K8sLifecycleAction action, Func<CancellationToken, Task<bool>>? execute = null) =>
            Lifecycle.ChangeAsync(action, execute ?? (_ => Task.FromResult(true)), success => success, default);
    }

    private sealed class HoldFake : IKubernetesKeepAlive
    {
        public KubernetesHold? Session { get; private set; }
        public int Starts { get; private set; }
        public void Hold(string distro)
        {
            if (Session?.Distro == distro) return;
            Starts++;
            Session = new(distro);
        }
        public void Release() => Session = null;
    }

    private sealed class ProbeFake : IK8sStatusProbe
    {
        public Func<CancellationToken, Task<ClusterStatus>> Status = _ => Task.FromResult(Running());
        public Func<string, CancellationToken, Task<K8sFooterStatus>> Footer = (distro, _) =>
            Task.FromResult(new K8sFooterStatus { State = ClusterState.Running, Distro = distro });
        public int StatusCalls;
        public int FooterCalls;
        public int StateCalls;
        public bool IsKnownNotInstalled(string distro) => false;
        public Task<ClusterStatus> GetStatusAsync(CancellationToken ct)
        {
            StatusCalls++;
            return Status(ct);
        }
        public Task<K8sFooterStatus> ProbeFooterAsync(string distro, CancellationToken ct)
        {
            FooterCalls++;
            return Footer(distro, ct);
        }
        public Task<ClusterState> ProbeStateAsync(CancellationToken ct)
        {
            StateCalls++;
            return Task.FromResult(ClusterState.Running);
        }
    }
}
