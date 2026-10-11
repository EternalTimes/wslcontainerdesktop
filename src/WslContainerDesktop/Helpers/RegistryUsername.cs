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

namespace WslContainerDesktop.Helpers;

/// <summary>
/// Cleans up a registry username before it is handed to <c>wslc login</c>.
///
/// Docker Hub issues a Docker ID of 4 to 30 numbers and lower-case letters only — no special
/// characters and no spaces (<see href="https://docs.docker.com/faqs/accounts/">Docker account FAQ</see>).
/// When the typed name falls outside that rule, Docker Hub's token service does not report a
/// wrong password: it rejects the request as <c>unknown: malformed HTTP Authorization header</c>,
/// which surfaces as an opaque <c>E_FAIL</c> engine dump. The account is fine; only the name is
/// not. Folding case and full-width input here turns most of those dead ends into a login that
/// works, and <see cref="IsSupportedByDockerHub"/> names what still cannot work.
/// </summary>
public static class RegistryUsername
{
    /// <summary>Shortest Docker Hub account name Docker will issue.</summary>
    public const int MinDockerHubNameLength = 4;

    /// <summary>Longest Docker Hub account name Docker will issue.</summary>
    public const int MaxDockerHubNameLength = 30;

    /// <summary>True when <paramref name="host"/> names Docker Hub rather than a private registry.</summary>
    public static bool IsDockerHub(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var h = host.Trim();
        return h.Contains("docker.io", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prepares the typed username for the engine: trims it, folds full-width characters back
    /// to their half-width ASCII form (a frequent Chinese-IME slip that produces an otherwise
    /// unusable name), and lower-cases it for Docker Hub, whose usernames are always lower-case.
    /// </summary>
    /// <param name="raw">The text as typed in the login dialog.</param>
    /// <param name="dockerHub">True when the target registry is Docker Hub.</param>
    public static string Normalize(string? raw, bool dockerHub)
    {
        var text = ToHalfWidth(raw ?? string.Empty).Trim();
        return dockerHub ? text.ToLowerInvariant() : text;
    }

    /// <summary>
    /// True when Docker Hub can actually accept <paramref name="user"/>. Docker Hub's own rule is
    /// 4 to 30 characters of numbers and lower-case letters only, with no special characters or
    /// spaces. Beyond the account-creation rule, anything Docker Hub cannot parse is reported as a
    /// malformed Authorization header rather than as a wrong password, so catching it here keeps
    /// that opaque engine error out of the login flow. Always true for non-Docker-Hub registries,
    /// whose username rules vary.
    /// </summary>
    public static bool IsSupportedByDockerHub(string? user, bool dockerHub)
    {
        if (!dockerHub)
        {
            return true;
        }

        if (user is null || user.Length is < MinDockerHubNameLength or > MaxDockerHubNameLength)
        {
            return false;
        }

        foreach (var c in user)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Folds full-width (U+FF01-U+FF5E) and ideographic-space (U+3000) characters to their
    /// half-width ASCII equivalents, leaving every other character untouched.
    /// </summary>
    private static string ToHalfWidth(string value)
    {
        var buffer = new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buffer[i] = c switch
            {
                // Full-width ASCII block (U+FF01-U+FF5E) and the ideographic space (U+3000).
                >= '\uFF01' and <= '\uFF5E' => (char)(c - 0xFEE0),
                '\u3000' => ' ',
                _ => c,
            };
        }

        return new string(buffer);
    }
}
