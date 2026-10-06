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

using WslContainerDesktop.Models;

namespace WslContainerDesktop.Services;

/// <summary>What a background Kubernetes footer refresh may do next.</summary>
public enum K8sFooterAction
{
    /// <summary>Launch nothing; report <see cref="K8sFooterDecision.State"/>.</summary>
    Report,

    /// <summary>Ask WSL which distributions are running (<c>wsl --list --running</c>, which starts none).</summary>
    CheckRunning,

    /// <summary>Run the status probe inside the distribution, which is already running.</summary>
    Probe,
}

/// <summary>A footer refresh step and, for <see cref="K8sFooterAction.Report"/>, the state to show.</summary>
/// <param name="Action">What to do next.</param>
/// <param name="State">The state to report when nothing is probed.</param>
/// <param name="DistroStopped">True when k3s can't be running because its distribution isn't running.</param>
/// <param name="NotKeptRunning">
/// True when the app isn't keeping the distribution running, so it is not eligible for probing.
/// </param>
public readonly record struct K8sFooterDecision(
    K8sFooterAction Action,
    ClusterState State = ClusterState.Unknown,
    bool DistroStopped = false,
    bool NotKeptRunning = false);

/// <summary>
/// Pure rules for the background Kubernetes footer status. A background poll must never start a WSL
/// distribution: repeated cold starts reset Windows' display and sleep idle timers in the issue #126
/// reproduction. So the footer only follows the distribution where k3s was seen installed (the
/// pin), and probes it only while the app's keep-alive session holds it. A distribution nothing
/// holds can stop between "is it running?" and the probe (WSL's idle timer fires 15–16 s after the
/// last session, right when the next poll lands), and the probe would then start it again.
/// </summary>
public static class K8sFooterProbePolicy
{
    /// <summary>Decides what may happen before any process is launched.</summary>
    /// <param name="host">The resolved k3s host, or null when the WSL registry couldn't be read.</param>
    /// <param name="pinnedDistro">The distribution where k3s was seen installed, if any.</param>
    /// <param name="knownNotInstalled">True when a probe this session found k3s missing from the host.</param>
    /// <param name="stoppedByUser">
    /// True when the user stopped k3s from the app, which then doesn't keep its distribution running.
    /// </param>
    public static K8sFooterDecision BeforeRunningCheck(
        KubernetesHost? host, string? pinnedDistro, bool knownNotInstalled, bool stoppedByUser)
    {
        if (host is null)
        {
            return Report(ClusterState.Unknown);
        }

        if (!host.CanHost)
        {
            return Report(ClusterState.NoDistribution);
        }

        if (string.IsNullOrWhiteSpace(pinnedDistro) || string.IsNullOrWhiteSpace(host.DistroName))
        {
            return Report(ClusterState.Unknown);
        }

        if (knownNotInstalled)
        {
            return Report(ClusterState.NotInstalled);
        }

        // Nothing holds the distribution, so it could never be probed; report what the user chose.
        return stoppedByUser
            ? Report(ClusterState.Stopped)
            : new K8sFooterDecision(K8sFooterAction.CheckRunning);
    }

    /// <summary>Decides whether the host distribution may be probed, given the distributions running now.</summary>
    /// <param name="distro">The host distribution's registered name.</param>
    /// <param name="running">The running distributions, or null when they couldn't be listed.</param>
    /// <param name="held">True when the app's keep-alive session holds <paramref name="distro"/>.</param>
    public static K8sFooterDecision AfterRunningCheck(string distro, IReadOnlySet<string>? running, bool held)
    {
        if (running is null)
        {
            return Report(ClusterState.Unknown);
        }

        if (!running.Any(name => string.Equals(name, distro, StringComparison.OrdinalIgnoreCase)))
        {
            return new K8sFooterDecision(K8sFooterAction.Report, ClusterState.Stopped, DistroStopped: true);
        }

        return held
            ? new K8sFooterDecision(K8sFooterAction.Probe)
            : new K8sFooterDecision(K8sFooterAction.Report, ClusterState.Stopped, NotKeptRunning: true);
    }

    private static K8sFooterDecision Report(ClusterState state) => new(K8sFooterAction.Report, state);
}
