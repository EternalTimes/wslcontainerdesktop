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

/// <summary>
/// Facade for managing a single-node k3s Kubernetes cluster inside a WSL distro. All privileged
/// operations run via <c>wsl.exe -u root</c> (no Linux password required); k3s bundles kubectl.
/// The actual work is delegated to focused collaborators - <see cref="K8sInstaller"/> (lifecycle
/// and versions), <see cref="K8sResourceClient"/> (status, queries, single-object actions), and
/// <see cref="PortForwardManager"/> (port-forward sessions) - over a shared <see cref="WslRootShell"/>,
/// plus <see cref="KubernetesKeepAlive"/>, which keeps the k3s distribution running while the app runs.
/// </summary>
public sealed class KubernetesService : IKubernetesService
{
    private readonly ISettingsService _settings;
    private readonly WslDistroInventory _distros;
    private readonly IWslSystemService _wsl;
    private readonly ILogger<KubernetesService> _logger;
    private readonly K8sInstaller _installer;
    private readonly K8sResourceClient _resources;
    private readonly PortForwardManager _portForwards;
    private readonly KubernetesKeepAlive _keepAlive;

    /// <summary>Creates the k3s collaborators around the configured WSL distro and shared root shell.</summary>
    public KubernetesService(ISettingsService settings, WslDistroInventory distros, IWslSystemService wsl, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _distros = distros;
        _wsl = wsl;
        _logger = loggerFactory.CreateLogger<KubernetesService>();
        var shell = new WslRootShell(settings);
        _installer = new K8sInstaller(shell);
        _resources = new K8sResourceClient(shell, distros, loggerFactory.CreateLogger<K8sResourceClient>());
        _portForwards = new PortForwardManager(shell);
        _keepAlive = new KubernetesKeepAlive(loggerFactory.CreateLogger<KubernetesKeepAlive>());
    }

    // ---- Status ----
    /// <inheritdoc/>
    public async Task<ClusterStatus> GetStatusAsync(CancellationToken ct = default)
    {
        // A cluster the user stopped stays stopped: k3s's service starts whenever its distribution
        // boots, so just looking at it mustn't start the distribution. Only a distribution WSL
        // reports already running is checked.
        if (_settings.KubernetesStoppedByUser && !string.IsNullOrWhiteSpace(_settings.WslDistro) &&
            _distros.ResolveKubernetesHost() is { CanHost: true, DistroName: { } distro })
        {
            var running = await _wsl.GetRunningDistributionsAsync(ct).ConfigureAwait(false);
            if (running is null || !running.Any(name => string.Equals(name, distro, StringComparison.OrdinalIgnoreCase)))
            {
                return new ClusterStatus { State = ClusterState.Stopped, Distro = distro };
            }
        }

        var status = await _resources.GetStatusAsync(ct).ConfigureAwait(false);

        // k3s is running again, for example because a terminal started its distribution and its
        // service started with it. The Stop no longer applies: keep it running like any running
        // cluster, so the app never shows a running cluster it won't keep running.
        if (status.State is ClusterState.Running or ClusterState.Starting && _settings.KubernetesStoppedByUser)
        {
            SetStoppedByUser(false);
            await SyncKeepAliveAsync(ct).ConfigureAwait(false);
        }

        return status;
    }

    /// <inheritdoc/>
    public async Task<K8sFooterStatus> GetFooterStatusAsync(CancellationToken ct = default)
    {
        var host = _distros.ResolveKubernetesHost();
        var distro = host?.DistroName;
        var decision = K8sFooterProbePolicy.BeforeRunningCheck(
            host,
            _settings.WslDistro,
            distro is not null && _resources.IsKnownNotInstalled(distro),
            _settings.KubernetesStoppedByUser);
        if (decision.Action == K8sFooterAction.Report)
        {
            return new K8sFooterStatus { State = decision.State, Distro = distro };
        }

        try
        {
            var running = await _wsl.GetRunningDistributionsAsync(ct).ConfigureAwait(false);
            var held = string.Equals(_keepAlive.HeldDistro, distro, StringComparison.OrdinalIgnoreCase);
            decision = K8sFooterProbePolicy.AfterRunningCheck(distro!, running, held);
            if (decision.Action == K8sFooterAction.Report)
            {
                return new K8sFooterStatus
                {
                    State = decision.State,
                    Distro = distro,
                    DistroStopped = decision.DistroStopped,
                    NotKeptRunning = decision.NotKeptRunning,
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Listing running WSL distributions failed.");
            return new K8sFooterStatus { State = ClusterState.Unknown, Distro = distro };
        }

        var status = await _resources.ProbeFooterAsync(distro!, ct).ConfigureAwait(false);

        // k3s was removed outside the app: stop keeping its old distribution running. Releasing
        // never starts anything, so it is safe from a poll.
        if (status.State == ClusterState.NotInstalled &&
            string.Equals(_keepAlive.HeldDistro, distro, StringComparison.OrdinalIgnoreCase))
        {
            _keepAlive.Release();
        }

        return status;
    }

    // ---- Keeping the distribution running while the app runs ----
    /// <inheritdoc/>
    public async Task SyncKeepAliveAsync(CancellationToken ct = default)
    {
        try
        {
            var newlyHeld = await Task.Run(SyncKeepAlive, ct).ConfigureAwait(false);

            // The pin means k3s was seen there, but it can be removed outside the app. The session
            // has just started the distribution, so this check starts nothing more.
            if (newlyHeld is not null &&
                await _resources.ProbeStateAsync(ct).ConfigureAwait(false) == ClusterState.NotInstalled)
            {
                _keepAlive.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update whether the k3s distribution is kept running.");
        }
    }

    /// <inheritdoc/>
    public void ReleaseKeepAlive() => _keepAlive.Release();

    /// <summary>Holds or releases the keep-alive session; returns the distribution newly held, if any.</summary>
    private string? SyncKeepAlive()
    {
        var host = _distros.ResolveKubernetesHost();
        var distro = K8sKeepAlivePolicy.DistroToKeepRunning(
            host,
            _settings.WslDistro,
            _settings.KubernetesStoppedByUser,
            host?.DistroName is { } name && _resources.IsKnownNotInstalled(name));
        if (distro is null)
        {
            _keepAlive.Release();
            return null;
        }

        var before = _keepAlive.HeldDistro;
        _keepAlive.Hold(distro);
        return !string.Equals(before, distro, StringComparison.OrdinalIgnoreCase) && _keepAlive.HeldDistro is not null
            ? distro
            : null;
    }

    private void SetStoppedByUser(bool stopped)
    {
        if (_settings.KubernetesStoppedByUser != stopped)
        {
            _settings.KubernetesStoppedByUser = stopped;
            _settings.Save();
        }
    }

    // ---- Install / lifecycle ----
    /// <inheritdoc/>
    public async Task<K3sInstallResult> InstallAsync(string? expectedInstallerHash, Action<string> onOutput, CancellationToken ct = default)
    {
        var result = await _installer.InstallAsync(expectedInstallerHash, onOutput, ct).ConfigureAwait(false);
        if (result.Success)
        {
            SetStoppedByUser(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public Task<K3sInstallResult> UpgradeAsync(string? version, string? expectedInstallerHash, Action<string> onOutput, CancellationToken ct = default) =>
        _installer.UpgradeAsync(version, expectedInstallerHash, onOutput, ct);

    /// <inheritdoc/>
    public Task<string?> GetInstalledVersionAsync(CancellationToken ct = default) => _installer.GetInstalledVersionAsync(ct);
    /// <inheritdoc/>
    public Task<string?> GetLatestStableVersionAsync(CancellationToken ct = default) => _installer.GetLatestStableVersionAsync(ct);
    /// <inheritdoc/>
    public Task<string?> GetChannelVersionAsync(string channel, CancellationToken ct = default) => _installer.GetChannelVersionAsync(channel, ct);
    /// <inheritdoc/>
    public async Task<CommandResult> UninstallAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        var result = await _installer.UninstallAsync(onOutput, ct).ConfigureAwait(false);
        if (result.Success)
        {
            SetStoppedByUser(false);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<CommandResult> StartAsync(CancellationToken ct = default)
    {
        SetStoppedByUser(false);
        await SyncKeepAliveAsync(ct).ConfigureAwait(false);
        return await _installer.StartAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<CommandResult> StopAsync(CancellationToken ct = default)
    {
        SetStoppedByUser(true);
        var result = await _installer.StopAsync(ct).ConfigureAwait(false);
        await SyncKeepAliveAsync(ct).ConfigureAwait(false);
        return result;
    }

    // ---- Resource list queries ----
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sNode>> GetNodesAsync(CancellationToken ct = default) => _resources.GetNodesAsync(ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sPod>> GetPodsAsync(string? ns = null, CancellationToken ct = default) => _resources.GetPodsAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sDeployment>> GetDeploymentsAsync(string? ns = null, CancellationToken ct = default) => _resources.GetDeploymentsAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sService>> GetServicesAsync(string? ns = null, CancellationToken ct = default) => _resources.GetServicesAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sIngress>> GetIngressesAsync(string? ns = null, CancellationToken ct = default) => _resources.GetIngressesAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sPvc>> GetPvcsAsync(string? ns = null, CancellationToken ct = default) => _resources.GetPvcsAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sConfigMap>> GetConfigMapsAsync(string? ns = null, CancellationToken ct = default) => _resources.GetConfigMapsAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sSecret>> GetSecretsAsync(string? ns = null, CancellationToken ct = default) => _resources.GetSecretsAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sJob>> GetJobsAsync(string? ns = null, CancellationToken ct = default) => _resources.GetJobsAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<K8sCronJob>> GetCronJobsAsync(string? ns = null, CancellationToken ct = default) => _resources.GetCronJobsAsync(ns, ct);
    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default) => _resources.GetNamespacesAsync(ct);
    /// <inheritdoc/>
    public Task<CommandResult> ApplyManifestAsync(string yaml, CancellationToken ct = default) => _resources.ApplyManifestAsync(yaml, ct);

    // ---- Single-object actions ----
    /// <inheritdoc/>
    public Task<CommandResult> DeleteResourceAsync(string kind, string ns, string name, CancellationToken ct = default) => _resources.DeleteResourceAsync(kind, ns, name, ct);
    /// <inheritdoc/>
    public Task<CommandResult> ScaleDeploymentAsync(string ns, string name, int replicas, CancellationToken ct = default) => _resources.ScaleDeploymentAsync(ns, name, replicas, ct);
    /// <inheritdoc/>
    public Task<CommandResult> RestartDeploymentAsync(string ns, string name, CancellationToken ct = default) => _resources.RestartDeploymentAsync(ns, name, ct);
    /// <inheritdoc/>
    public Task<CommandResult> SetCronJobSuspendAsync(string ns, string name, bool suspend, CancellationToken ct = default) => _resources.SetCronJobSuspendAsync(ns, name, suspend, ct);
    /// <inheritdoc/>
    public Task<CommandResult> TriggerCronJobAsync(string ns, string name, CancellationToken ct = default) => _resources.TriggerCronJobAsync(ns, name, ct);
    /// <inheritdoc/>
    public Task<CommandResult> GetResourceYamlAsync(string kind, string ns, string name, CancellationToken ct = default) => _resources.GetResourceYamlAsync(kind, ns, name, ct);
    /// <inheritdoc/>
    public Task<CommandResult> DescribeResourceAsync(string kind, string ns, string name, CancellationToken ct = default) => _resources.DescribeResourceAsync(kind, ns, name, ct);
    /// <inheritdoc/>
    public Task<CommandResult> GetPodLogsAsync(string ns, string name, int tailLines, CancellationToken ct = default) => _resources.GetPodLogsAsync(ns, name, tailLines, ct);

    // ---- Port forwarding ----
    /// <inheritdoc/>
    public bool StartPortForward(PortForward forward) => _portForwards.StartPortForward(forward);
    /// <inheritdoc/>
    public void StopPortForward(string id) => _portForwards.StopPortForward(id);
    /// <inheritdoc/>
    public void StopAllPortForwards() => _portForwards.StopAllPortForwards();
}
