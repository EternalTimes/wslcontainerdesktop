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

namespace WslContainerDesktop.Services;

/// <summary>
/// Pure rule for which WSL distribution the app keeps running for k3s while the app runs: the pinned
/// distribution where k3s was seen installed, unless the user stopped Kubernetes from the app, k3s was
/// since found missing there, or the distribution can't host k3s.
/// </summary>
public static class K8sKeepAlivePolicy
{
    /// <summary>Returns the distribution to keep running, or null to keep none.</summary>
    /// <param name="host">The resolved k3s host, or null when the WSL registry couldn't be read.</param>
    /// <param name="pinnedDistro">The distribution where k3s was seen installed, if any.</param>
    /// <param name="stoppedByUser">True after the user stopped Kubernetes from the app.</param>
    /// <param name="knownNotInstalled">True when a probe this session found k3s missing from the host.</param>
    public static string? DistroToKeepRunning(KubernetesHost? host, string? pinnedDistro, bool stoppedByUser, bool knownNotInstalled) =>
        host is { CanHost: true, DistroName: { Length: > 0 } distro }
        && !string.IsNullOrWhiteSpace(pinnedDistro)
        && !stoppedByUser
        && !knownNotInstalled
            ? distro
            : null;
}
