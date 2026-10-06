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

/// <summary>UI-thread-owned startup observation; cancellation immediately permits a replacement.</summary>
public sealed class K8sStartupWatcher(
    IWindowVisibility visibility,
    Func<CancellationToken, Task<ClusterStatus>> observe,
    Action<ClusterStatus> apply,
    Action ready,
    Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
{
    private CancellationTokenSource? _current;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delayAsync ?? Task.Delay;
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    public async Task StartAsync()
    {
        if (_current is not null)
        {
            return;
        }

        using var cts = new CancellationTokenSource();
        _current = cts;
        try
        {
            var delay = FirstDelay;
            while (true)
            {
                await visibility.WhenViewableAsync(cts.Token);
                await _delay(delay, cts.Token);
                await visibility.WhenViewableAsync(cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                var status = await observe(cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_current, cts))
                {
                    return;
                }

                apply(status);
                if (status.State != ClusterState.Starting)
                {
                    // ready may start the resource poller, which also stops startup watching.
                    _current = null;
                    if (status.State == ClusterState.Running)
                    {
                        ready();
                    }

                    return;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // A later visit owns its own watcher; this one may only dispose itself.
        }
        finally
        {
            if (ReferenceEquals(_current, cts))
            {
                _current = null;
            }
        }
    }

    public void Stop()
    {
        var previous = _current;
        _current = null;
        previous?.Cancel();
    }
}
