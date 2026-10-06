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
/// Thread-safe <see cref="IWindowVisibility"/> state. The main window reports its shown/minimized
/// state and the display's power state; this combines them. It starts not viewable, because the app
/// can start hidden in the tray.
/// </summary>
public sealed class WindowVisibility : IWindowVisibility
{
    private readonly object _gate = new();
    private bool _windowShown;
    private bool _minimized;
    private bool _displayOn = true;
    private TaskCompletionSource _viewable = NewSource();

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public bool IsViewable
    {
        get
        {
            lock (_gate)
            {
                return Compute();
            }
        }
    }

    /// <summary>Records whether the main window is shown and whether it is minimized.</summary>
    public void SetWindowState(bool shown, bool minimized) => Update(() =>
    {
        _windowShown = shown;
        _minimized = minimized;
    });

    /// <summary>Records whether the display is on (dimmed counts as on).</summary>
    public void SetDisplayOn(bool on) => Update(() => _displayOn = on);

    /// <inheritdoc/>
    public Task WhenViewableAsync(CancellationToken ct = default)
    {
        Task task;
        lock (_gate)
        {
            task = _viewable.Task;
        }

        return task.IsCompleted ? task : task.WaitAsync(ct);
    }

    private void Update(Action apply)
    {
        bool changed;
        lock (_gate)
        {
            var before = Compute();
            apply();
            var now = Compute();
            changed = before != now;
            if (changed)
            {
                if (now)
                {
                    _viewable.TrySetResult();
                }
                else
                {
                    _viewable = NewSource();
                }
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Compute() => _windowShown && !_minimized && _displayOn;

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
