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

using WslContainerDesktop.Helpers;
using Xunit;

namespace WslContainerDesktop.Tests.Helpers;

/// <summary>
/// Covers registry username normalization and support checks. Docker Hub rejects a username
/// containing upper-case, non-ASCII or most punctuation characters with
/// "malformed HTTP Authorization header" instead of a wrong-password error, so the name must
/// be folded to lower case before it reaches the engine.
/// </summary>
public sealed class RegistryUsernameTests
{
    [Theory]
    [InlineData("Tiramisucat", "tiramisucat")]
    [InlineData("  Tiramisucat  ", "tiramisucat")]
    [InlineData("MiXeD.CaSe_99", "mixed.case_99")]
    public void DockerHubNamesAreFoldedToLowerCase(string typed, string expected) =>
        Assert.Equal(expected, RegistryUsername.Normalize(typed, dockerHub: true));

    [Theory]
    [InlineData("Contoso.Deploy", "Contoso.Deploy")]
    [InlineData("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000")]
    public void OtherRegistriesKeepTheCaseTheyWereGiven(string typed, string expected) =>
        Assert.Equal(expected, RegistryUsername.Normalize(typed, dockerHub: false));

    [Theory]
    [InlineData("Ｔｉｒａｍｉｓｕ", "Tiramisu")]   // full-width latin
    [InlineData("Ｔiramisu", "Tiramisu")]           // full-width prefix only
    [InlineData("tiramisu　", "tiramisu")]         // ideographic space
    public void FullWidthInputFromAnImeIsFoldedBackToAscii(string typed, string expected) =>
        Assert.Equal(expected, RegistryUsername.Normalize(typed, dockerHub: false));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingNamesNormalizeToEmpty(string? typed) =>
        Assert.Equal(string.Empty, RegistryUsername.Normalize(typed, dockerHub: true));

    [Theory]
    [InlineData("tiramisucat")]
    [InlineData("abcd")]
    [InlineData("user99")]
    [InlineData("1234")]
    [InlineData("a12345678901234567890123456789")]  // exactly 30
    public void NamesWithinTheDockerHubRuleAreSupported(string user) =>
        Assert.True(RegistryUsername.IsSupportedByDockerHub(user, dockerHub: true));

    [Theory]
    [InlineData("Tiramisucat")]   // upper case: the reported failure
    [InlineData("User")]
    [InlineData("user name")]    // embedded space
    [InlineData("user!name")]
    [InlineData("user.name")]    // Docker IDs allow no special characters at all
    [InlineData("user_name")]
    [InlineData("user-name")]
    [InlineData("me@example.com")]
    [InlineData("用户")]           // non-ASCII
    [InlineData("Ｕｓｅｒ")]        // full-width
    [InlineData("")]             // empty
    [InlineData("abc")]          // under 4 characters
    [InlineData("a123456789012345678901234567890")]  // 31 characters
    public void NamesDockerHubRejectsAreNotSupported(string user) =>
        Assert.False(RegistryUsername.IsSupportedByDockerHub(user, dockerHub: true));

    [Fact]
    public void TheDockerHubRuleIsFourToThirtyCharacters()
    {
        Assert.Equal(4, RegistryUsername.MinDockerHubNameLength);
        Assert.Equal(30, RegistryUsername.MaxDockerHubNameLength);
        Assert.True(RegistryUsername.IsSupportedByDockerHub(
            new string('a', RegistryUsername.MinDockerHubNameLength), dockerHub: true));
        Assert.True(RegistryUsername.IsSupportedByDockerHub(
            new string('a', RegistryUsername.MaxDockerHubNameLength), dockerHub: true));
        Assert.False(RegistryUsername.IsSupportedByDockerHub(
            new string('a', RegistryUsername.MaxDockerHubNameLength + 1), dockerHub: true));
    }

    [Theory]
    [InlineData("Tiramisucat")]
    [InlineData("用户")]
    [InlineData("")]
    public void OtherRegistriesImposeNoCharsetOfTheirOwnHere(string user) =>
        Assert.True(RegistryUsername.IsSupportedByDockerHub(user, dockerHub: false));

    [Theory]
    [InlineData("docker.io", true)]
    [InlineData("registry-1.docker.io", true)]
    [InlineData("index.docker.io", true)]
    [InlineData("https://index.docker.io/v1/", true)]
    [InlineData("DOCKER.IO", true)]
    [InlineData("contoso.azurecr.io", false)]
    [InlineData("ghcr.io", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DockerHubHostsAreRecognized(string? host, bool expected) =>
        Assert.Equal(expected, RegistryUsername.IsDockerHub(host));
}
