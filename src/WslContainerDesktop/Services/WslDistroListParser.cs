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

/// <summary>
/// Pure parsing and decision logic for WSL distributions, kept free of registry and process I/O
/// so it can be unit-tested on any platform.
/// </summary>
public static class WslDistroListParser
{
    /// <summary>
    /// Parses the tabular output of <c>wsl -l -v</c> into distro rows. WSL prints this table with
    /// fixed English headers and state words in every display language; only the "no installed
    /// distributions" message is localized. A row is still accepted only when its last column is a
    /// WSL version (1 or 2), so that message, or any header, is never mistaken for a distro.
    /// </summary>
    public static List<WslDistroStatus> Parse(string output)
    {
        var result = new List<WslDistroStatus>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Replace('\r', ' ').TrimEnd();
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var isDefault = line.TrimStart().StartsWith('*');
            var tokens = line.Replace("*", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length < 3 || !int.TryParse(tokens[^1], out var version) || version is not (1 or 2))
            {
                continue;
            }

            result.Add(new WslDistroStatus
            {
                Name = string.Join(' ', tokens[..^2]),
                State = tokens[^2],
                Version = version,
                IsDefault = isDefault,
            });
        }

        return result;
    }

    /// <summary>
    /// Parses <c>wsl --list --running --quiet</c> output, one distribution name per line, into a
    /// case-insensitive set. Callers only test membership for a registered name, so a message line
    /// can never be mistaken for a running distribution.
    /// </summary>
    public static IReadOnlySet<string> ParseRunningNames(string output)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            // NULs appear if wsl.exe ignored WSL_UTF8 and wrote UTF-16, which is read here as UTF-8.
            var name = raw.Replace("\0", string.Empty, StringComparison.Ordinal).Trim().TrimStart('\uFEFF').Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }
}
