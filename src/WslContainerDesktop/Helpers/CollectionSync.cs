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

using System.Collections.ObjectModel;

namespace WslContainerDesktop.Helpers;

/// <summary>In-place collection updates that keep a bound list's scroll position.</summary>
public static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="target"/> match <paramref name="items"/> without raising a Reset, which
    /// is what snaps a bound list back to the top. Rows are replaced, added or removed in place.
    /// </summary>
    public static void ReplaceAll<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i >= target.Count)
            {
                target.Add(items[i]);
            }
            else if (!ReferenceEquals(target[i], items[i]))
            {
                target[i] = items[i];
            }
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
