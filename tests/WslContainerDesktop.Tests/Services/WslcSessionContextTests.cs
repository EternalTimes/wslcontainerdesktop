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

using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

public sealed class WslcSessionContextTests
{
    [Fact]
    public void AddSessionSelector_UsesDefaultUnlessNamedSessionIsSelected()
    {
        var context = new WslcSessionContext();

        Assert.Equal(["list", "--format", "json"], context.AddSessionSelector(["list", "--format", "json"]));

        context.Select("SDK Session");
        Assert.Equal(["--session", "SDK Session", "list", "--format", "json"],
            context.AddSessionSelector(["list", "--format", "json"]));

        context.Select(null);
        Assert.Equal(["list"], context.AddSessionSelector(["list"]));
    }
}
