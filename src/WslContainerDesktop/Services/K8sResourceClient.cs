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

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WslContainerDesktop.Models;

namespace WslContainerDesktop.Services;

/// <summary>
/// Reads cluster/resource state and performs single-object actions via <c>k3s kubectl</c>.
/// Covers status probes, list queries for each resource kind, apply, and the
/// delete/scale/restart/cron/yaml/describe/logs operations.
/// </summary>
public sealed class K8sResourceClient(WslRootShell shell, WslDistroInventory distros, ILogger<K8sResourceClient> logger) : IK8sStatusProbe
{
    // Whether k3s was found installed in each distribution by the latest probe this session.
    private readonly ConcurrentDictionary<string, bool> _installedByDistro = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a probe this session found k3s isn't installed in <paramref name="distro"/>.</summary>
    public bool IsKnownNotInstalled(string distro) =>
        _installedByDistro.TryGetValue(distro, out var installed) && !installed;

    private void Observe(string? distro, ClusterState state)
    {
        if (string.IsNullOrWhiteSpace(distro))
        {
            return;
        }

        if (state == ClusterState.NotInstalled)
        {
            _installedByDistro[distro] = false;
        }
        else if (state is ClusterState.Stopped or ClusterState.Starting or ClusterState.Running)
        {
            _installedByDistro[distro] = true;
        }
    }

    // ---- Status ---------------------------------------------------------

    /// <summary>Reads detailed k3s cluster status for the dashboard.</summary>
    public async Task<ClusterStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var host = distros.ResolveKubernetesHost();
        if (host is { CanHost: false })
        {
            return new ClusterStatus
            {
                State = ClusterState.NoDistribution,
                HostProblem = host.Problem,
                Distro = host.DistroName ?? "-",
            };
        }

        // The real distro name (e.g. "Ubuntu") when the registry was readable, not "default".
        var distroLabel = host?.DistroName ?? shell.DistroLabel;

        try
        {
            var script = K8sStatusProtocol.BuildProbeScript(
                K8sStatusProtocol.NodesMarker, "k3s kubectl get nodes -o json");

            var r = await shell.RunAsync(script, ct, K8sStatusProtocol.ProbeTimeout).ConfigureAwait(false);
            var output = r.StandardOutput;
            var status = K8sStatusProtocol.ParseResult(r, distroLabel);
            if (status.State == ClusterState.Unknown)
            {
                logger.LogWarning("Kubernetes cluster status unavailable: {Diagnostic}", status.Message);
                return status;
            }

            Observe(host?.DistroName, status.State);
            if (status.State != ClusterState.Running)
            {
                return status;
            }

            var nodeJson = K8sStatusProtocol.SectionAfter(output, K8sStatusProtocol.NodesMarker);
            var node = K8sParser.Nodes(nodeJson).FirstOrDefault();

            return new ClusterStatus
            {
                State = ClusterState.Running,
                Distro = distroLabel,
                NodeName = node?.Name ?? "-",
                KubernetesVersion = node?.Version ?? "-",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Kubernetes cluster status probe failed.");
            return new ClusterStatus { State = ClusterState.Unknown, Message = ex.Message };
        }
    }

    /// <summary>
    /// Probes the compact status (state + pod counts) used by the app footer in <paramref name="distro"/>,
    /// which must be the pinned host distribution. Running this starts the distribution if it isn't
    /// running, so the background footer only calls it for a distribution the keep-alive holds
    /// (see <see cref="K8sFooterProbePolicy"/>, issue #126).
    /// </summary>
    public async Task<K8sFooterStatus> ProbeFooterAsync(string distro, CancellationToken ct = default)
    {
        try
        {
            var script = K8sStatusProtocol.BuildProbeScript(
                K8sStatusProtocol.PodsMarker, "k3s kubectl get pods -A -o json");

            var r = await shell.RunAsync(script, ct, K8sStatusProtocol.ProbeTimeout).ConfigureAwait(false);
            var output = r.StandardOutput;
            var status = K8sStatusProtocol.ParseResult(r, distro);
            var state = status.State;
            if (state == ClusterState.Unknown)
            {
                logger.LogDebug("Kubernetes footer status unavailable: {Diagnostic}", status.Message);
            }

            Observe(distro, state);
            if (state != ClusterState.Running)
            {
                return new K8sFooterStatus { State = state, Distro = distro };
            }

            var podJson = K8sStatusProtocol.SectionAfter(output, K8sStatusProtocol.PodsMarker);
            var pods = K8sParser.Pods(podJson);

            return new K8sFooterStatus
            {
                State = ClusterState.Running,
                Distro = distro,
                PodsRunning = pods.Count(p => p.IsRunning),
                PodsTotal = pods.Count,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Kubernetes footer status probe failed.");
            return new K8sFooterStatus { State = ClusterState.Unknown, Distro = distro };
        }
    }

    // ---- Resource list queries ------------------------------------------

    /// <summary>
    /// Reads only whether k3s is installed, starting, stopped or running in the host distribution,
    /// listing nothing. This starts the distribution if it isn't running, so call it only on purpose.
    /// </summary>
    public async Task<ClusterState> ProbeStateAsync(CancellationToken ct = default)
    {
        var host = distros.ResolveKubernetesHost();
        try
        {
            var r = await shell.RunAsync(K8sStatusProtocol.BuildStateProbeScript(), ct, K8sStatusProtocol.ProbeTimeout).ConfigureAwait(false);
            var status = K8sStatusProtocol.ParseResult(r, host?.DistroName ?? shell.DistroLabel);
            var state = status.State;
            if (state == ClusterState.Unknown)
            {
                logger.LogDebug("Kubernetes state unavailable: {Diagnostic}", status.Message);
            }

            Observe(host?.DistroName, state);
            return state;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Kubernetes state probe failed.");
            return ClusterState.Unknown;
        }
    }

    /// <summary>Lists Kubernetes nodes.</summary>
    public async Task<IReadOnlyList<K8sNode>> GetNodesAsync(CancellationToken ct = default)
    {
        var r = await shell.RunAsync("k3s kubectl get nodes -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Nodes(r.StandardOutput) : Array.Empty<K8sNode>();
    }

    /// <summary>Lists pods, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sPod>> GetPodsAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get pods {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Pods(r.StandardOutput) : Array.Empty<K8sPod>();
    }

    /// <summary>Lists deployments, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sDeployment>> GetDeploymentsAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get deployments {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Deployments(r.StandardOutput) : Array.Empty<K8sDeployment>();
    }

    /// <summary>Lists services, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sService>> GetServicesAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get services {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Services(r.StandardOutput) : Array.Empty<K8sService>();
    }

    /// <summary>Lists ingresses, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sIngress>> GetIngressesAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get ingress {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Ingresses(r.StandardOutput) : Array.Empty<K8sIngress>();
    }

    /// <summary>Lists persistent volume claims, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sPvc>> GetPvcsAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get pvc {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Pvcs(r.StandardOutput) : Array.Empty<K8sPvc>();
    }

    /// <summary>Lists config maps, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sConfigMap>> GetConfigMapsAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get configmaps {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.ConfigMaps(r.StandardOutput) : Array.Empty<K8sConfigMap>();
    }

    /// <summary>Lists secrets by metadata only, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sSecret>> GetSecretsAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get secrets {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Secrets(r.StandardOutput) : Array.Empty<K8sSecret>();
    }

    /// <summary>Lists jobs, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sJob>> GetJobsAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get jobs {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Jobs(r.StandardOutput) : Array.Empty<K8sJob>();
    }

    /// <summary>Lists cron jobs, optionally scoped to a namespace.</summary>
    public async Task<IReadOnlyList<K8sCronJob>> GetCronJobsAsync(string? ns = null, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get cronjobs {WslRootShell.NsSelector(ns)} -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.CronJobs(r.StandardOutput) : Array.Empty<K8sCronJob>();
    }

    /// <summary>Lists Kubernetes namespaces for filters and creation targets.</summary>
    public async Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        var r = await shell.RunAsync("k3s kubectl get namespaces -o json", ct).ConfigureAwait(false);
        return r.Success ? K8sParser.Namespaces(r.StandardOutput) : Array.Empty<string>();
    }

    /// <summary>Applies user-provided YAML to the cluster.</summary>
    public Task<CommandResult> ApplyManifestAsync(string yaml, CancellationToken ct = default) =>
        shell.RunWithStdinAsync("k3s kubectl apply -f -", yaml, ct);

    // ---- Single-object actions ------------------------------------------

    /// <summary>Deletes one Kubernetes resource by kind, namespace and name.</summary>
    public Task<CommandResult> DeleteResourceAsync(string kind, string ns, string name, CancellationToken ct = default) =>
        shell.RunAsync($"k3s kubectl delete {WslRootShell.SafeKind(kind)} {WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)}", ct);

    /// <summary>Sets a deployment replica count.</summary>
    public Task<CommandResult> ScaleDeploymentAsync(string ns, string name, int replicas, CancellationToken ct = default) =>
        shell.RunAsync($"k3s kubectl scale deployment {WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)} --replicas={replicas}", ct);

    /// <summary>Restarts a deployment by issuing a rollout restart.</summary>
    public Task<CommandResult> RestartDeploymentAsync(string ns, string name, CancellationToken ct = default) =>
        shell.RunAsync($"k3s kubectl rollout restart deployment {WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)}", ct);

    /// <summary>Suspends or resumes a cron job.</summary>
    public Task<CommandResult> SetCronJobSuspendAsync(string ns, string name, bool suspend, CancellationToken ct = default)
    {
        var patch = suspend ? "{\"spec\":{\"suspend\":true}}" : "{\"spec\":{\"suspend\":false}}";
        return shell.RunAsync($"k3s kubectl patch cronjob {WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)} -p {WslRootShell.ShellEscape(patch)}", ct);
    }

    /// <summary>Creates a one-off job from a cron job template.</summary>
    public Task<CommandResult> TriggerCronJobAsync(string ns, string name, CancellationToken ct = default)
    {
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var jobName = $"{name}-manual-{stamp}";
        return shell.RunAsync(
            $"k3s kubectl create job {WslRootShell.ShellEscape(jobName)} --from=cronjob/{WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)}", ct);
    }

    /// <summary>Reads a resource as YAML for inspection or editing.</summary>
    public async Task<CommandResult> GetResourceYamlAsync(string kind, string ns, string name, CancellationToken ct = default)
    {
        var r = await shell.RunAsync($"k3s kubectl get {WslRootShell.SafeKind(kind)} {WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)} -o yaml", ct)
            .ConfigureAwait(false);

        if (!r.Success)
        {
            return r;
        }

        // Return a manifest that is safe to `kubectl apply` again: drop the read-only
        // status block and server-managed metadata that otherwise triggers strict-decode
        // or optimistic-concurrency errors when the edited YAML is re-applied.
        return new CommandResult
        {
            ExitCode = r.ExitCode,
            StandardOutput = K8sManifestSanitizer.Sanitize(r.StandardOutput),
            StandardError = r.StandardError,
        };
    }

    /// <summary>Runs <c>kubectl describe</c> for one resource.</summary>
    public Task<CommandResult> DescribeResourceAsync(string kind, string ns, string name, CancellationToken ct = default) =>
        shell.RunAsync($"k3s kubectl describe {WslRootShell.SafeKind(kind)} {WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)}", ct);

    /// <summary>Reads recent pod logs with a bounded tail count.</summary>
    public Task<CommandResult> GetPodLogsAsync(string ns, string name, int tailLines, CancellationToken ct = default) =>
        shell.RunAsync($"k3s kubectl logs {WslRootShell.ShellEscape(name)}{WslRootShell.NsArg(ns)} --all-containers=true --tail={tailLines}", ct);
}
