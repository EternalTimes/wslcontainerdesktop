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

/// <summary>
/// Issue #126: the background Kubernetes footer poll started WSL's default distribution on every
/// cycle, and each start reset Windows' display and sleep idle timers. These rules make sure a
/// background poll only probes the distribution where k3s was seen installed, and only while the
/// app's keep-alive session holds it running (a distribution nothing holds can stop between the
/// running check and the probe, which would then start it again).
/// </summary>
public sealed class K8sFooterProbePolicyTests
{
    private static readonly KubernetesHost Ubuntu = new(KubernetesHostProblem.None, "Ubuntu");
    private static readonly IReadOnlySet<string> UbuntuRunning = new HashSet<string>(StringComparer.Ordinal) { "UBUNTU" };

    [Fact]
    public void AnUnreadableRegistryLaunchesNothing()
    {
        var decision = K8sFooterProbePolicy.BeforeRunningCheck(null, "Ubuntu", knownNotInstalled: false, stoppedByUser: false);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.Unknown, decision.State);
    }

    [Theory]
    [InlineData(KubernetesHostProblem.NoDistributions)]
    [InlineData(KubernetesHostProblem.NoDefault)]
    [InlineData(KubernetesHostProblem.PinnedMissing)]
    [InlineData(KubernetesHostProblem.Wsl1)]
    [InlineData(KubernetesHostProblem.ManagedByTool)]
    public void AHostThatCantRunK3sLaunchesNothing(KubernetesHostProblem problem)
    {
        var decision = K8sFooterProbePolicy.BeforeRunningCheck(new KubernetesHost(problem, "Ubuntu"), "Ubuntu", false, false);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.NoDistribution, decision.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutAPinnedDistributionTheDefaultDistributionIsNeverTouched(string? pinned)
    {
        var decision = K8sFooterProbePolicy.BeforeRunningCheck(Ubuntu, pinned, knownNotInstalled: false, stoppedByUser: false);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.Unknown, decision.State);
    }

    [Fact]
    public void K3sFoundMissingThisSessionLaunchesNothing()
    {
        var decision = K8sFooterProbePolicy.BeforeRunningCheck(Ubuntu, "Ubuntu", knownNotInstalled: true, stoppedByUser: false);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.NotInstalled, decision.State);
    }

    [Fact]
    public void AClusterStoppedFromTheAppIsReportedStoppedWithoutLaunchingAnything()
    {
        var decision = K8sFooterProbePolicy.BeforeRunningCheck(Ubuntu, "Ubuntu", knownNotInstalled: false, stoppedByUser: true);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.Stopped, decision.State);
        Assert.False(decision.DistroStopped);
        Assert.False(decision.NotKeptRunning);
    }

    [Fact]
    public void APinnedHostIsOnlyCheckedForBeingRunning()
    {
        Assert.Equal(K8sFooterAction.CheckRunning, K8sFooterProbePolicy.BeforeRunningCheck(Ubuntu, "Ubuntu", false, false).Action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedRunningCheckLaunchesNothingMore(bool held)
    {
        var decision = K8sFooterProbePolicy.AfterRunningCheck("Ubuntu", running: null, held);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.Unknown, decision.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // The keep-alive session can end before the app hears about it.
    public void AStoppedDistributionIsReportedInsteadOfStarted(bool held)
    {
        var decision = K8sFooterProbePolicy.AfterRunningCheck("Ubuntu", new HashSet<string> { "Debian" }, held);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.Stopped, decision.State);
        Assert.True(decision.DistroStopped);
    }

    [Fact]
    public void ARunningDistributionTheAppDoesntHoldIsNotProbed()
    {
        var decision = K8sFooterProbePolicy.AfterRunningCheck("Ubuntu", UbuntuRunning, held: false);

        Assert.Equal(K8sFooterAction.Report, decision.Action);
        Assert.Equal(ClusterState.Stopped, decision.State);
        Assert.True(decision.NotKeptRunning);
        Assert.False(decision.DistroStopped);
    }

    [Fact]
    public void AHeldRunningDistributionIsProbedWhateverTheNameCasing()
    {
        Assert.Equal(K8sFooterAction.Probe, K8sFooterProbePolicy.AfterRunningCheck("Ubuntu", UbuntuRunning, held: true).Action);
    }

    [Fact]
    public void TheProbeRunsOnlyWhenK3sIsPinnedThereMeantToRunAndHeldRunningByTheApp()
    {
        KubernetesHost?[] hosts = [null, new KubernetesHost(KubernetesHostProblem.Wsl1, "Ubuntu"), Ubuntu];
        string?[] pins = [null, "Ubuntu"];
        bool[] flags = [false, true];
        IReadOnlySet<string>?[] runningSets = [null, new HashSet<string>(), new HashSet<string> { "Debian" }, UbuntuRunning];

        foreach (var host in hosts)
        foreach (var pin in pins)
        foreach (var missing in flags)
        foreach (var stopped in flags)
        foreach (var running in runningSets)
        foreach (var held in flags)
        {
            var decision = K8sFooterProbePolicy.BeforeRunningCheck(host, pin, missing, stopped);
            if (decision.Action == K8sFooterAction.CheckRunning)
            {
                decision = K8sFooterProbePolicy.AfterRunningCheck(host!.DistroName!, running, held);
            }

            var expected = host is { CanHost: true } && pin is not null && !missing && !stopped &&
                ReferenceEquals(running, UbuntuRunning) && held;
            Assert.Equal(expected, decision.Action == K8sFooterAction.Probe);
        }
    }
}
