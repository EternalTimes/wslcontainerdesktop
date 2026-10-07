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
/// One UI language the app can run in.
/// </summary>
/// <param name="Tag">
/// BCP-47 tag matching the folder under <c>Strings/</c> and the <c>&lt;Resource Language="…" /&gt;</c>
/// entry in Package.appxmanifest. Empty means "follow the system", which is the default.
/// </param>
/// <param name="ResourceKey">String resource holding this option's display name in its own language.</param>
internal sealed record AppLanguageOption(string Tag, string ResourceKey);

/// <summary>
/// The single source of truth for which UI languages exist. Adding one means: create
/// <c>Strings/&lt;tag&gt;/Resources.resw</c>, declare the tag in Package.appxmanifest's
/// <c>&lt;Resources&gt;</c> block, and add it here — no other code needs to change, because the
/// resource compiler discovers every locale folder that is declared.
/// </summary>
internal static class AppLanguage
{
    /// <summary>Language tag meaning "do not override; use the system's display language".</summary>
    public const string SystemDefault = "";

    /// <summary>Selectable languages, in the order the Settings page lists them.</summary>
    public static readonly IReadOnlyList<AppLanguageOption> Supported = new[]
    {
        new AppLanguageOption(SystemDefault, "Settings_Language_Option_System"),
        new AppLanguageOption("en-US", "Settings_Language_Option_English"),
        new AppLanguageOption("zh-Hans", "Settings_Language_Option_ChineseSimplified"),
    };

    /// <summary>
    /// Maps a stored or user-supplied tag onto a supported one. Unknown, empty and malformed values
    /// fall back to <see cref="SystemDefault"/> rather than throwing: this comes from a JSON file on
    /// disk, which may be hand-edited, and a bad language must never stop the app from starting.
    /// </summary>
    public static string Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return SystemDefault;
        }

        foreach (var option in Supported)
        {
            if (string.Equals(option.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return option.Tag;
            }
        }

        return SystemDefault;
    }

    /// <summary>Index of <paramref name="tag"/> in <see cref="Supported"/>, defaulting to 0 (system).</summary>
    public static int IndexOf(string? tag)
    {
        var normalized = Normalize(tag);
        for (var i = 0; i < Supported.Count; i++)
        {
            if (Supported[i].Tag == normalized)
            {
                return i;
            }
        }

        return 0;
    }
}