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
/// Docker IDs use 4 to 30 lower-case letters and digits
/// (<see href="https://docs.docker.com/faqs/accounts/">Docker account FAQ</see>).
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

        var address = host.Trim();
        if (address.Contains('\\') || address.Contains('@'))
        {
            return false;
        }

        if (!address.Contains("://", StringComparison.Ordinal))
        {
            address = "https://" + address;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return string.Equals(uri.Host, "docker.io", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "index.docker.io", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "registry-1.docker.io", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trims the username, folding full-width ASCII and upper case only for Docker Hub.
    /// Other registries keep their original characters and case.
    /// </summary>
    /// <param name="raw">The text as typed in the login dialog.</param>
    /// <param name="dockerHub">True when the target registry is Docker Hub.</param>
    public static string Normalize(string? raw, bool dockerHub)
    {
        var text = raw ?? string.Empty;
        return dockerHub ? ToHalfWidth(text).Trim().ToLowerInvariant() : text.Trim();
    }

    /// <summary>
    /// Checks the normalized username against Docker Hub's Docker ID format.
    /// Always true for other registries, whose username rules vary.
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
