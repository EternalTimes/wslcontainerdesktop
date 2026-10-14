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

/// <summary>Process-wide selection for the WSLC session used by container operations.</summary>
public sealed class WslcSessionContext
{
    private string? _selectedSessionName;

    /// <summary>Gets the selected session name, or null to use the CLI default session.</summary>
    public string? SelectedSessionName => Volatile.Read(ref _selectedSessionName);

    /// <summary>Selects a named session, or null to use the CLI default session.</summary>
    public void Select(string? sessionName) =>
        Volatile.Write(ref _selectedSessionName, string.IsNullOrWhiteSpace(sessionName) ? null : sessionName);

    /// <summary>Prepends the CLI session selector when an explicit session is selected.</summary>
    public IEnumerable<string> AddSessionSelector(IEnumerable<string> arguments)
    {
        var sessionName = SelectedSessionName;
        if (sessionName is null)
        {
            return arguments;
        }

        return ["--session", sessionName, .. arguments];
    }
}
