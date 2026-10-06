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
/// Whether anyone can currently see the app's main window. Polling that only feeds visible UI
/// (live stats, Kubernetes resource lists, the Kubernetes footer) waits on this, so the app does no
/// such work while it is hidden in the tray, minimized, or the display is off.
/// </summary>
public interface IWindowVisibility
{
    /// <summary>True while the main window is shown, not minimized, and the display is on.</summary>
    bool IsViewable { get; }

    /// <summary>Raised, on any thread, when <see cref="IsViewable"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Completes when the app is viewable; immediately if it already is.</summary>
    Task WhenViewableAsync(CancellationToken ct = default);
}
