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
/// Maps raw <c>wslc.exe</c> policy errors to messages a user can act on without seeing engine jargon.
/// </summary>
public static class CommandErrorText
{
    private static readonly string[] ErrorCodePrefixes = ["Error code:", "Error code：", "错误代码:", "错误代码："];

    /// <summary>Returns a trimmed message, replacing recognized WSL policy failures with friendly guidance.</summary>
    public static string Friendly(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Contains("WSL container is disabled by the computer policy", StringComparison.OrdinalIgnoreCase))
        {
            return "Your organization disabled WSL containers by policy. Contact your administrator to enable WSL containers.";
        }

        if (text.Contains("registry allowlist policy is invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "Your organization's WSLContainerRegistryAllowlist policy is invalid. " +
                "Contact your administrator to correct the registry allowlist policy.";
        }

        if (text.Contains("is blocked by the computer policy", StringComparison.OrdinalIgnoreCase))
        {
            return "This registry is not on your organization's approved registry list (WSLContainerRegistryAllowlist). " +
                "Use an approved registry or contact your administrator.";
        }

        return text.Trim();
    }

    /// <summary>True when the raw engine error says WSL containers are disabled by machine policy.</summary>
    public static bool IsWslContainersDisabled(string text) =>
        text.Contains("WSL container is disabled by the computer policy", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Recognizes the CLI's missing-login result for a logout command. Callers must still
    /// verify that no corresponding credentials remain before reporting completion.
    /// </summary>
    public static bool IsNotLoggedIn(CommandResult result)
    {
        if (result.ExitCode != 1)
        {
            return false;
        }

        var raw = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            // Match the complete error-code value, not a token embedded in a nested error
            // such as Wsl/Service/HCS/ERROR_NOT_FOUND or quoted diagnostic text.
            foreach (var prefix in ErrorCodePrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(trimmed[prefix.Length..].Trim(), "ERROR_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
