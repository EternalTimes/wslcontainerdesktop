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
/// While the app runs it keeps the k3s distribution running, so pods don't stop when WSL would
/// otherwise idle it out. Only a distribution where k3s was seen installed is kept, and never one
/// the user stopped from the app.
/// </summary>
public sealed class K8sKeepAlivePolicyTests
{
    private static readonly KubernetesHost Ubuntu = new(KubernetesHostProblem.None, "Ubuntu");

    [Fact]
    public void KeepsThePinnedDistributionWhereK3sIsInstalled()
    {
        Assert.Equal("Ubuntu", K8sKeepAlivePolicy.DistroToKeepRunning(Ubuntu, "Ubuntu", stoppedByUser: false, knownNotInstalled: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void KeepsNothingWhenK3sWasNeverSeenInstalled(string? pinned)
    {
        Assert.Null(K8sKeepAlivePolicy.DistroToKeepRunning(Ubuntu, pinned, false, false));
    }

    [Fact]
    public void KeepsNothingAfterTheUserStoppedKubernetes()
    {
        Assert.Null(K8sKeepAlivePolicy.DistroToKeepRunning(Ubuntu, "Ubuntu", stoppedByUser: true, knownNotInstalled: false));
    }

    [Fact]
    public void KeepsNothingOnceK3sIsFoundMissing()
    {
        Assert.Null(K8sKeepAlivePolicy.DistroToKeepRunning(Ubuntu, "Ubuntu", stoppedByUser: false, knownNotInstalled: true));
    }

    [Fact]
    public void KeepsNothingWhenTheRegistryCantBeRead()
    {
        Assert.Null(K8sKeepAlivePolicy.DistroToKeepRunning(null, "Ubuntu", false, false));
    }

    [Theory]
    [InlineData(KubernetesHostProblem.PinnedMissing)]
    [InlineData(KubernetesHostProblem.Wsl1)]
    [InlineData(KubernetesHostProblem.ManagedByTool)]
    public void KeepsNothingInADistributionThatCantRunK3s(KubernetesHostProblem problem)
    {
        Assert.Null(K8sKeepAlivePolicy.DistroToKeepRunning(new KubernetesHost(problem, "Ubuntu"), "Ubuntu", false, false));
    }
}
