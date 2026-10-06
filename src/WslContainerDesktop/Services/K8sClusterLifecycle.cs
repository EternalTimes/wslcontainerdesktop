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

using Microsoft.Extensions.Logging;
using WslContainerDesktop.Models;

namespace WslContainerDesktop.Services;

/// <summary>Serializes cluster observations and explicit lifecycle operations, including their I/O.</summary>
public sealed class K8sClusterLifecycle(
    ISettingsService settings,
    Func<KubernetesHost?> resolveHost,
    Func<CancellationToken, Task<IReadOnlySet<string>?>> runningDistributions,
    IK8sStatusProbe probes,
    IKubernetesKeepAlive keepAlive,
    ILogger<K8sClusterLifecycle> logger)
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _holdGate = new();
    private long _revision;
    private volatile bool _shutdown;

    /// <summary>Whether recurring page queries still have a held host.</summary>
    public bool CanObserve => !_shutdown && !settings.KubernetesStoppedByUser &&
        keepAlive.Session is { } session && SameDistro(session.Distro, settings.WslDistro);

    public async Task<ClusterStatus> GetStatusAsync(bool observationOnly, CancellationToken ct)
    {
        var revision = Interlocked.Read(ref _revision);
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(revision))
            {
                return Superseded();
            }

            var host = resolveHost();
            var distro = host?.DistroName;
            KubernetesHold? session = null;
            if (observationOnly)
            {
                var eligibility = await EligibilityAsync(host, ct).ConfigureAwait(false);
                session = eligibility.Session;
                if (eligibility.Decision.Action != K8sFooterAction.Probe)
                {
                    return Unobserved(eligibility.Decision, host);
                }
            }
            else if (settings.KubernetesStoppedByUser)
            {
                // Unknown inventory is not permission to boot a cluster the user stopped.
                if (host is not { CanHost: true, DistroName: not null })
                {
                    return new ClusterStatus { State = ClusterState.Unknown, Message = "Cannot check the stopped cluster's WSL distribution." };
                }

                var running = await runningDistributions(ct).ConfigureAwait(false);
                if (running is null)
                {
                    return new ClusterStatus { State = ClusterState.Unknown, Distro = distro!, Message = "Could not list running WSL distributions." };
                }

                if (!running.Any(name => SameDistro(name, distro)))
                {
                    return new ClusterStatus { State = ClusterState.Stopped, Distro = distro! };
                }
            }

            ct.ThrowIfCancellationRequested();
            if (!IsCurrent(revision) || (observationOnly && !ReferenceEquals(session, keepAlive.Session)))
            {
                return Superseded();
            }

            var status = await probes.GetStatusAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!IsCurrent(revision) || !SameDistro(distro, resolveHost()?.DistroName))
            {
                return Superseded();
            }

            if (observationOnly)
            {
                return ReferenceEquals(session, keepAlive.Session)
                    ? status
                    : Unobserved(new(K8sFooterAction.Report, ClusterState.Stopped, NotKeptRunning: true), host);
            }

            // Discovery and its hold acquisition are one operation: Stop cannot complete between them.
            // The short commit shares shutdown's fence, so late discovery cannot change settings after exit.
            lock (_holdGate)
            {
                if (!IsCurrent(revision))
                {
                    return Superseded();
                }

                if (status.IsInstalled && string.IsNullOrWhiteSpace(settings.WslDistro) &&
                    status.Distro is not ("" or "-" or "default"))
                {
                    settings.WslDistro = status.Distro;
                    settings.Save();
                }

                if (status.State is ClusterState.Running or ClusterState.Starting)
                {
                    SetStopped(false);
                }
            }

            await SyncCoreAsync(ct).ConfigureAwait(false);
            return IsCurrent(revision) ? status : Superseded();
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task<K8sFooterStatus> GetFooterStatusAsync(CancellationToken ct)
    {
        var revision = Interlocked.Read(ref _revision);
        // The shared monitor also drives container health. Never hold up that loop behind an install.
        if (!await _operations.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return new K8sFooterStatus { State = ClusterState.Working, Distro = settings.WslDistro };
        }

        try
        {
            var host = resolveHost();
            if (!IsCurrent(revision))
            {
                return new K8sFooterStatus { Distro = host?.DistroName };
            }

            var (decision, session) = await EligibilityAsync(host, ct).ConfigureAwait(false);
            if (decision.Action != K8sFooterAction.Probe || host?.DistroName is null)
            {
                return new K8sFooterStatus
                {
                    State = decision.State, Distro = host?.DistroName,
                    DistroStopped = decision.DistroStopped, NotKeptRunning = decision.NotKeptRunning,
                };
            }

            ct.ThrowIfCancellationRequested();
            if (!IsCurrent(revision) || !ReferenceEquals(session, keepAlive.Session) ||
                !SameDistro(host.DistroName, resolveHost()?.DistroName))
            {
                return new K8sFooterStatus { Distro = host?.DistroName };
            }

            var status = await probes.ProbeFooterAsync(host.DistroName, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!IsCurrent(revision) || !ReferenceEquals(session, keepAlive.Session) ||
                !SameDistro(host.DistroName, resolveHost()?.DistroName))
            {
                return new K8sFooterStatus { Distro = host.DistroName };
            }

            if (status.State == ClusterState.NotInstalled)
            {
                ReleaseSession(session);
            }

            return status;
        }
        finally
        {
            _operations.Release();
        }
    }

    private async Task<(K8sFooterDecision Decision, KubernetesHold? Session)> EligibilityAsync(
        KubernetesHost? host, CancellationToken ct)
    {
        var distro = host?.DistroName;
        var decision = K8sFooterProbePolicy.BeforeRunningCheck(host, settings.WslDistro,
            distro is not null && probes.IsKnownNotInstalled(distro), settings.KubernetesStoppedByUser);
        if (decision.Action == K8sFooterAction.Report)
        {
            return (decision, null);
        }

        var session = keepAlive.Session;
        if (session is null || !SameDistro(session.Distro, distro))
        {
            return (new(K8sFooterAction.Report, ClusterState.Stopped, NotKeptRunning: true), null);
        }

        var running = await runningDistributions(ct).ConfigureAwait(false);
        decision = K8sFooterProbePolicy.AfterRunningCheck(distro!, running, ReferenceEquals(session, keepAlive.Session));
        return (decision, session);
    }

    public async Task SyncKeepAliveAsync(CancellationToken ct)
    {
        var revision = Interlocked.Read(ref _revision);
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsCurrent(revision))
            {
                await SyncCoreAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    private async Task SyncCoreAsync(CancellationToken ct)
    {
        try
        {
            var before = keepAlive.Session;
            await Task.Run(() =>
            {
                lock (_holdGate)
                {
                    if (_shutdown)
                    {
                        return;
                    }

                    var host = resolveHost();
                    var distro = K8sKeepAlivePolicy.DistroToKeepRunning(host, settings.WslDistro,
                        settings.KubernetesStoppedByUser,
                        host?.DistroName is { } name && probes.IsKnownNotInstalled(name));
                    if (distro is null)
                    {
                        keepAlive.Release();
                    }
                    else
                    {
                        keepAlive.Hold(distro);
                    }
                }
            }, ct).ConfigureAwait(false);

            var session = keepAlive.Session;
            if (!_shutdown && session is not null && !ReferenceEquals(before, session) &&
                await probes.ProbeStateAsync(ct).ConfigureAwait(false) == ClusterState.NotInstalled)
            {
                ReleaseSession(session);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update whether the k3s distribution is kept running.");
        }
    }

    /// <summary>Invalidates old reads before queueing a lifecycle command behind the async gate.</summary>
    public async Task<T> ChangeAsync<T>(K8sLifecycleAction action,
        Func<CancellationToken, Task<T>> execute, Func<T, bool> succeeded, CancellationToken ct)
    {
        Interlocked.Increment(ref _revision);
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_shutdown, this);
            var host = resolveHost();
            if (action is K8sLifecycleAction.Start or K8sLifecycleAction.Stop)
            {
                SetStopped(action == K8sLifecycleAction.Stop);
            }

            if (action == K8sLifecycleAction.Start)
            {
                await SyncCoreAsync(ct).ConfigureAwait(false);
            }

            var result = await execute(ct).ConfigureAwait(false);
            if (succeeded(result))
            {
                if (action is K8sLifecycleAction.Install or K8sLifecycleAction.Uninstall)
                {
                    SetStopped(false);
                }

                if (action == K8sLifecycleAction.Install && string.IsNullOrWhiteSpace(settings.WslDistro) &&
                    host is { CanHost: true, DistroName: not null })
                {
                    settings.WslDistro = host.DistroName;
                    settings.Save();
                }

                if (action == K8sLifecycleAction.Uninstall)
                {
                    settings.WslDistro = null;
                    settings.Save();
                    ReleaseSession(keepAlive.Session);
                }
            }

            return result;
        }
        finally
        {
            // Even a failed/canceled Stop must not silently restore automatic startup.
            if (action == K8sLifecycleAction.Stop)
            {
                ReleaseSession(keepAlive.Session);
            }

            Interlocked.Increment(ref _revision);
            _operations.Release();
        }
    }

    public async Task UseDefaultDistributionAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _revision);
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_shutdown, this);
            settings.WslDistro = null;
            settings.KubernetesStoppedByUser = false;
            settings.Save();
            ReleaseSession(keepAlive.Session);
        }
        finally
        {
            Interlocked.Increment(ref _revision);
            _operations.Release();
        }
    }

    public void Shutdown()
    {
        lock (_holdGate)
        {
            _shutdown = true;
            Interlocked.Increment(ref _revision);
            keepAlive.Release();
        }
    }

    private void ReleaseSession(KubernetesHold? session)
    {
        lock (_holdGate)
        {
            if (ReferenceEquals(session, keepAlive.Session))
            {
                keepAlive.Release();
            }
        }
    }

    private bool IsCurrent(long revision) => !_shutdown && revision == Interlocked.Read(ref _revision);

    private void SetStopped(bool stopped)
    {
        if (settings.KubernetesStoppedByUser != stopped)
        {
            settings.KubernetesStoppedByUser = stopped;
            settings.Save();
        }
    }

    private static bool SameDistro(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static ClusterStatus Superseded() => new()
    {
        State = ClusterState.Unknown,
        Message = "Cluster state changed during the check. Refresh to see its current state.",
    };

    private static ClusterStatus Unobserved(K8sFooterDecision decision, KubernetesHost? host) => new()
    {
        State = decision.State,
        Distro = host?.DistroName ?? "-",
        HostProblem = host?.Problem ?? default,
        Message = decision.NotKeptRunning
            ? "The WSL session is no longer held. Refresh or Start to reconnect; automatic checks are paused."
            : decision.State == ClusterState.Unknown
                ? "Cluster status is unavailable without discovery. Open or refresh the Kubernetes page."
                : string.Empty,
    };
}

/// <summary>Explicit changes that invalidate older observations.</summary>
public enum K8sLifecycleAction { Start, Stop, Install, Upgrade, Uninstall }
