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

/// <summary>Identifies Docker Hub endpoints when selecting logout targets and credential keys.</summary>
internal static class RegistryServerAddress
{
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
}
