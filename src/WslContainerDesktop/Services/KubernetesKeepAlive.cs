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

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WslContainerDesktop.Services;

/// <summary>
/// Keeps the k3s distribution running while the app runs. WSL keeps a distribution up only while a
/// WSL session is open (systemd services such as k3s don't count) and stops it once idle, so this
/// owns one idle session: <c>wsl.exe -d &lt;distro&gt; -u root -e sh -c 'exec cat &gt;/dev/null'</c>.
/// </summary>
/// <remarks>
/// The session can't outlive the app: the app holds its stdin open and never writes to it, so when
/// the app exits, normally or not, <c>cat</c> reads end-of-file and the session ends. The process is
/// also in <see cref="ChildProcessJob"/> as a second guard. Nothing restarts it after an unexpected
/// exit (for example <c>wsl --shutdown</c>), so it can never become a loop that keeps booting WSL and
/// resetting Windows' idle timers (issue #126).
/// </remarks>
public sealed class KubernetesKeepAlive(ILogger<KubernetesKeepAlive> logger) : IDisposable
{
    private const string HoldCommand = "exec cat >/dev/null";
    private static readonly TimeSpan ReleaseWait = TimeSpan.FromSeconds(5);

    // Serializes Hold/Release so concurrent requests can't start two sessions.
    private readonly object _operationGate = new();
    private readonly object _stateGate = new();
    private Process? _process;
    private string? _distro;

    /// <summary>The distribution currently kept running, or null.</summary>
    public string? HeldDistro
    {
        get
        {
            lock (_stateGate)
            {
                return _process is null ? null : _distro;
            }
        }
    }

    /// <summary>Keeps <paramref name="distro"/> running, releasing any other distribution held before.</summary>
    public void Hold(string distro)
    {
        lock (_operationGate)
        {
            Process? previous;
            lock (_stateGate)
            {
                if (_process is not null && string.Equals(_distro, distro, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                previous = _process;
                _process = null;
                _distro = null;
            }

            Close(previous);

            var process = new Process
            {
                StartInfo = WslRootShell.BaseStartInfoFor(distro, HoldCommand),
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += static (_, _) => { };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    logger.LogDebug("k3s keep-alive session for {Distro}: {Line}", distro, e.Data);
                }
            };
            process.Exited += (_, _) => OnExited(process, distro);

            lock (_stateGate)
            {
                try
                {
                    if (!process.Start())
                    {
                        process.Dispose();
                        logger.LogWarning("Could not start the session that keeps {Distro} running for k3s.", distro);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    process.Dispose();
                    logger.LogWarning(ex, "Could not start the session that keeps {Distro} running for k3s.", distro);
                    return;
                }

                _process = process;
                _distro = distro;
            }

            ChildProcessJob.Shared?.TryAssign(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            logger.LogInformation("Keeping WSL distribution {Distro} running for k3s while the app runs.", distro);
        }
    }

    /// <summary>Stops keeping any distribution running; WSL then stops it once it is idle.</summary>
    public void Release()
    {
        lock (_operationGate)
        {
            Process? process;
            string? distro;
            lock (_stateGate)
            {
                process = _process;
                distro = _distro;
                _process = null;
                _distro = null;
            }

            if (process is null)
            {
                return;
            }

            Close(process);
            logger.LogInformation("Stopped keeping WSL distribution {Distro} running for k3s.", distro);
        }
    }

    /// <summary>Releases the held session.</summary>
    public void Dispose() => Release();

    private void Close(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            // End-of-file on stdin ends `cat`, and with it the WSL session.
            process.StandardInput.Close();
            if (!process.WaitForExit(ReleaseWait))
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Closing the k3s keep-alive session failed; it may have already exited.");
        }
        finally
        {
            process.Dispose();
        }
    }

    private void OnExited(Process process, string distro)
    {
        lock (_stateGate)
        {
            if (!ReferenceEquals(_process, process))
            {
                return;
            }

            _process = null;
            _distro = null;
        }

        int? exitCode = null;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // The exit code isn't available; the warning below still records the exit.
        }

        logger.LogWarning(
            "The session keeping {Distro} running for k3s ended unexpectedly (exit code {ExitCode}). WSL stops the distribution once it is idle; opening the Kubernetes page, or the next app launch, keeps it running again.",
            distro, exitCode);
        process.Dispose();
    }
}
