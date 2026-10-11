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

/// <summary>Verifies actionable registry guidance and preservation of unrelated engine errors.</summary>
public sealed class CommandErrorTextTests
{
    private const string EngineDump =
        "来自引擎的错误信息: Get \"https://registry-1.docker.io/v2/\": unknown: malformed HTTP Authorization header\r\n" +
        "错误代码： E_FAIL\r\n";

    [Fact]
    public void MalformedAuthorizationHeaderBecomesActionableGuidance()
    {
        var friendly = CommandErrorText.Friendly(EngineDump);
        Assert.DoesNotContain("malformed HTTP Authorization header", friendly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("E_FAIL", friendly, StringComparison.Ordinal);
        Assert.Contains("username and password or access token", friendly, StringComparison.Ordinal);
        Assert.DoesNotContain("Docker Hub", friendly, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateRegistryAuthorizationErrorsDoNotShowDockerHubAccountRules()
    {
        const string raw = "Get https://registry.example.com/v2/: malformed HTTP Authorization header";
        var friendly = CommandErrorText.Friendly(raw);
        Assert.Contains("authorization header", friendly, StringComparison.Ordinal);
        Assert.DoesNotContain("Docker ID", friendly, StringComparison.Ordinal);
        Assert.DoesNotContain("4 to 30", friendly, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongPasswordStaysTheEnginesOwnMessage()
    {
        const string raw = "来自引擎的错误信息: Get \"https://registry-1.docker.io/v2/\": unauthorized: incorrect username or password";
        Assert.Equal(raw, CommandErrorText.Friendly(raw));
    }

    [Fact]
    public void PolicyErrorsStillWinOverRegistryGuidance()
    {
        const string raw = "registry.example.com is blocked by the computer policy: malformed HTTP Authorization header";
        Assert.Contains("approved registry list", CommandErrorText.Friendly(raw), StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedEngineErrorsStayUnchanged()
    {
        const string raw = "Create container failed: The system cannot find the file specified. Error code: ERROR_FILE_NOT_FOUND";
        Assert.Equal(raw, CommandErrorText.Friendly(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyEngineOutputStaysEmpty(string raw) =>
        Assert.Equal(string.Empty, CommandErrorText.Friendly(raw));
}
