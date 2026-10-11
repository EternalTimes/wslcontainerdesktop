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

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WslContainerDesktop.Helpers;

namespace WslContainerDesktop.Services;

/// <summary>Whether registry login metadata is present, absent, or could not be read.</summary>
public enum RegistryCredentialStatus
{
    /// <summary>All matching credential keys are absent.</summary>
    Missing,
    /// <summary>At least one matching credential exists.</summary>
    Present,
    /// <summary>No credential was found, but at least one key could not be checked.</summary>
    Unavailable,
}

/// <summary>Reads registry login state from the store where the wslc engine keeps it.</summary>
public interface IRegistryCredentialStore
{
    /// <summary>
    /// Returns true if the engine has a stored credential for <paramref name="server"/> (i.e. the
    /// user is logged in to it), with <paramref name="username"/> set to the stored account name
    /// when available. The secret itself is never read.
    /// </summary>
    bool IsLoggedIn(string server, out string? username);

    /// <summary>Checks login metadata without treating a credential-store read failure as absence.</summary>
    RegistryCredentialStatus GetLoginStatus(string server, out string? username);

    /// <summary>
    /// Reads the stored username and secret for <paramref name="server"/> so a caller can
    /// authenticate directly to a registry's HTTP API (e.g. to browse its catalog). Returns
    /// true only when a credential exists. The secret is returned to the caller but must never
    /// be logged or persisted. Used only for generic (non-Azure) registries; Azure registries
    /// browse via the signed-in `az` session instead.
    /// </summary>
    bool TryGetCredential(string server, out string? username, out string? password);
}

/// <summary>
/// Reads the wslc engine's registry credentials from the Windows Credential Manager. wslc's default
/// credential backend is <c>wincred</c>, which stores each registry login as a Generic credential
/// named <c>wslc-credential/&lt;server&gt;</c>. Login-state checks read only presence and the stored
/// username, so the indicator reflects CLI logins without copying the secret blob.
/// </summary>
public sealed class RegistryCredentialStore : IRegistryCredentialStore
{
    private const string TargetPrefix = "wslc-credential/";
    private const int CRED_TYPE_GENERIC = 1;
    private const int ERROR_NOT_FOUND = 1168;
    private readonly ILogger<RegistryCredentialStore> _logger;
    private readonly Func<string, (int ErrorCode, string? Username)> _readMetadata;

    /// <summary>Creates the read-only view of Windows registry credential metadata.</summary>
    public RegistryCredentialStore(ILogger<RegistryCredentialStore> logger)
    {
        _logger = logger;
        _readMetadata = ReadMetadata;
    }

    internal RegistryCredentialStore(ILogger<RegistryCredentialStore> logger,
        Func<string, (int ErrorCode, string? Username)> readMetadata)
    {
        _logger = logger;
        _readMetadata = readMetadata;
    }

    /// <inheritdoc/>
    public bool IsLoggedIn(string server, out string? username) =>
        GetLoginStatus(server, out username) == RegistryCredentialStatus.Present;

    /// <inheritdoc/>
    public RegistryCredentialStatus GetLoginStatus(string server, out string? username)
    {
        username = null;
        if (string.IsNullOrWhiteSpace(server))
        {
            return RegistryCredentialStatus.Missing;
        }

        var status = RegistryCredentialStatus.Missing;
        foreach (var candidate in CandidateServers(server))
        {
            var metadata = _readMetadata(TargetPrefix + candidate);
            if (metadata.ErrorCode == 0)
            {
                username = metadata.Username;
                return RegistryCredentialStatus.Present;
            }

            if (metadata.ErrorCode != ERROR_NOT_FOUND)
            {
                status = RegistryCredentialStatus.Unavailable;
            }
        }

        return status;
    }

    /// <inheritdoc/>
    public bool TryGetCredential(string server, out string? username, out string? password)
    {
        username = null;
        password = null;
        if (string.IsNullOrWhiteSpace(server))
        {
            return false;
        }

        foreach (var candidate in CandidateServers(server))
        {
            if (TryReadSecret(TargetPrefix + candidate, out username, out password))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks the original input, the engine's host-and-port key, and any matching Docker Hub aliases.
    /// </summary>
    private static IEnumerable<string> CandidateServers(string server)
    {
        var s = server.Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { s };
        yield return s;

        // The engine strips a URI scheme and path before choosing its credential key.
        // Keep the explicit port and case: Uri normalization would lose a default port.
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        var host = scheme >= 0 ? s[(scheme + 3)..] : s;
        var path = host.IndexOf('/');
        if (path >= 0)
        {
            host = host[..path];
        }

        if (host.Length > 0 && seen.Add(host))
        {
            yield return host;
        }

        if (RegistryServerAddress.IsDockerHub(s))
        {
            foreach (var alias in new[] { "docker.io", "index.docker.io", "registry-1.docker.io", "https://index.docker.io/v1/" })
            {
                if (seen.Add(alias))
                {
                    yield return alias;
                }
            }
        }
    }

    private (int ErrorCode, string? Username) ReadMetadata(string target)
    {
        var handle = IntPtr.Zero;
        try
        {
            if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out handle))
            {
                var error = Marshal.GetLastWin32Error();
                return (error == 0 ? -1 : error, null);
            }

            var cred = Marshal.PtrToStructure<CREDENTIAL>(handle);
            return (0, string.IsNullOrWhiteSpace(cred.UserName) ? null : cred.UserName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read credential {Target}.", target);
            return (-1, null);
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CredFree(handle);
            }
        }
    }

    /// <summary>Reads the secret blob (UTF-8) alongside the username. wslc's wincred backend
    /// stores the password as UTF-8 bytes in the credential blob.</summary>
    private bool TryReadSecret(string target, out string? username, out string? password)
    {
        username = null;
        password = null;
        var handle = IntPtr.Zero;
        try
        {
            if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out handle))
            {
                return false;
            }

            var cred = Marshal.PtrToStructure<CREDENTIAL>(handle);
            username = string.IsNullOrWhiteSpace(cred.UserName) ? null : cred.UserName;

            if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
            {
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                password = System.Text.Encoding.UTF8.GetString(bytes);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read credential secret {Target}.", target);
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CredFree(handle);
            }
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);

    /// <summary>
    /// Managed projection of the Win32 <c>CREDENTIAL</c> structure used to read registry secrets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
