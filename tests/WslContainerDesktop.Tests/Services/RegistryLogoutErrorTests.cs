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
using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

/// <summary>Rejects nested or unrelated engine errors when recognizing an absent registry login.</summary>
public sealed class RegistryLogoutErrorTests
{
    [Theory]
    [InlineData("未登录到 https://index.docker.io/v1/\r\n错误代码： ERROR_NOT_FOUND", true)]
    [InlineData("Not logged in to https://index.docker.io/v1/\nError code: ERROR_NOT_FOUND", true)]
    [InlineData("unauthorized: incorrect username or password", false)]
    [InlineData("Error code: Wsl/Service/HCS/ERROR_NOT_FOUND", false)]
    [InlineData("Engine dump mentions ERROR_NOT_FOUND", false)]
    [InlineData("Error code: ERROR_NOT_FOUND_EXTRA", false)]
    [InlineData("Diagnostic: ERROR_NOT_FOUND", false)]
    [InlineData("", false)]
    public void OnlyTheCompleteCliErrorCodeIdentifiesAnAbsentLogin(string raw, bool expected) =>
        Assert.Equal(expected, CommandErrorText.IsNotLoggedIn(new CommandResult { ExitCode = 1, StandardError = raw }));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void OtherExitCodesDoNotCountAsMissingLogin(int exitCode) =>
        Assert.False(CommandErrorText.IsNotLoggedIn(new CommandResult
        {
            ExitCode = exitCode,
            StandardError = "Error code: ERROR_NOT_FOUND",
        }));

    [Fact]
    public void MissingLoginCanBeReportedOnStandardOutput() =>
        Assert.True(CommandErrorText.IsNotLoggedIn(new CommandResult
        {
            ExitCode = 1,
            StandardOutput = "Not logged in to docker.io\nError code: ERROR_NOT_FOUND\n",
        }));

    [Fact]
    public void UnrelatedStandardErrorDoesNotBorrowAMissingLoginFromStandardOutput() =>
        Assert.False(CommandErrorText.IsNotLoggedIn(new CommandResult
        {
            ExitCode = 1,
            StandardError = "access denied",
            StandardOutput = "Error code: ERROR_NOT_FOUND",
        }));
}
