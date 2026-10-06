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
/// The k3s status probe reports its state with markers. While systemd brings k3s up after WSL starts
/// the distribution, the probe must report Starting rather than Stopped.
/// </summary>
public sealed class K8sStatusProtocolTests
{
    [Theory]
    [InlineData("@@STATE=notinstalled\n", ClusterState.NotInstalled)]
    [InlineData("@@STATE=starting\n", ClusterState.Starting)]
    [InlineData("@@STATE=stopped\n", ClusterState.Stopped)]
    [InlineData("@@STATE=running\n@@PODS\n{\"items\":[]}\n", ClusterState.Running)]
    [InlineData("", ClusterState.Unknown)]
    [InlineData("wsl: something went wrong\n", ClusterState.Unknown)]
    [InlineData("diagnostic mentions @@STATE=running\n", ClusterState.Unknown)]
    [InlineData("@@STATE=running-invalid\n", ClusterState.Unknown)]
    public void ParsesEachStateMarker(string output, ClusterState expected)
    {
        Assert.Equal(expected, K8sStatusProtocol.ParseState(output));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("wsl: distribution unavailable", 0)]
    [InlineData("wsl: distribution unavailable", 1)]
    [InlineData("diagnostic mentions @@STATE=running", 1)]
    public void MarkerlessOutputNeverReportsRunning(string output, int exitCode)
    {
        var result = K8sStatusProtocol.ParseResult(new CommandResult
        {
            ExitCode = exitCode, StandardOutput = output,
        }, "Ubuntu");
        Assert.Equal(ClusterState.Unknown, result.State);
        Assert.Equal("Ubuntu", result.Distro);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        if (output.Length > 0) Assert.Contains(output, result.Message);
    }

    [Theory]
    [InlineData("@@STATE=notinstalled", ClusterState.NotInstalled)]
    [InlineData("@@STATE=stopped", ClusterState.Stopped)]
    [InlineData("@@STATE=starting", ClusterState.Starting)]
    [InlineData("@@STATE=running\r\n@@NODES\r\n{\"items\":[]}", ClusterState.Running)]
    public void SuccessfulProbeRequiresRecognizedState(string output, ClusterState expected)
    {
        Assert.Equal(expected, K8sStatusProtocol.ParseResult(new CommandResult { StandardOutput = output }, "Ubuntu").State);
    }

    [Fact]
    public void FailedNodeQueryPreservesServiceEvidenceWithoutClaimingHealthyObservation()
    {
        var status = K8sStatusProtocol.ParseResult(new CommandResult
        {
            ExitCode = 1, StandardOutput = "@@STATE=running\n@@NODES\n",
            StandardError = "connection refused",
        }, "Ubuntu");
        Assert.Equal(ClusterState.Unknown, status.State);
        Assert.Contains("active service", status.Message);
        Assert.Contains("connection refused", status.Message);
    }

    [Fact]
    public void TimeoutIsUnknownWithDiagnostic()
    {
        var status = K8sStatusProtocol.ParseResult(new CommandResult
        {
            ExitCode = -1, StandardError = "The command timed out.",
        }, "Ubuntu");
        Assert.Equal(ClusterState.Unknown, status.State);
        Assert.Contains("timed out", status.Message);
        Assert.Equal(TimeSpan.FromSeconds(30), K8sStatusProtocol.ProbeTimeout);
    }

    [Fact]
    public void ReportsStartingWhileSystemdIsStillActivatingK3s()
    {
        var script = K8sStatusProtocol.BuildProbeScript(K8sStatusProtocol.PodsMarker, "k3s kubectl get pods -A -o json");

        var starting = script.IndexOf("[ \"$a\" = activating ]", StringComparison.Ordinal);
        var stopped = script.IndexOf("[ \"$a\" != active ]", StringComparison.Ordinal);
        Assert.True(starting >= 0, "the probe must recognize an activating k3s service");
        Assert.True(starting < stopped, "activating must be checked before the generic not-active branch");
        Assert.Contains(K8sStatusProtocol.StateStarting, script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStateOnlyProbeListsNothing()
    {
        var script = K8sStatusProtocol.BuildStateProbeScript();

        Assert.Contains(K8sStatusProtocol.StateRunning, script, StringComparison.Ordinal);
        Assert.DoesNotContain("kubectl", script, StringComparison.Ordinal);
        Assert.StartsWith(script, K8sStatusProtocol.BuildProbeScript(K8sStatusProtocol.PodsMarker, "true"), StringComparison.Ordinal);
    }
}
