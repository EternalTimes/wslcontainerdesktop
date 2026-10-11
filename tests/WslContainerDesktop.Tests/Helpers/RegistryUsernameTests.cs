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
/// Covers Docker Hub host recognition and username normalization without changing other registries.
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
    [InlineData("Ｔｉｒａｍｉｓｕ", "Ｔｉｒａｍｉｓｕ")]
    [InlineData("Ｔiramisu", "Ｔiramisu")]
    [InlineData("　Ｔiramisu　", "Ｔiramisu")]
    [InlineData("  Contoso.Deploy  ", "Contoso.Deploy")]
    public void OtherRegistriesOnlyTrimTheUsername(string typed, string expected) =>
        Assert.Equal(expected, RegistryUsername.Normalize(typed, dockerHub: false));

    [Theory]
    [InlineData("Ｔｉｒａｍｉｓｕ", "tiramisu")]   // full-width latin
    [InlineData("Ｔiramisu", "tiramisu")]           // full-width prefix only
    [InlineData("tiramisu　", "tiramisu")]         // ideographic space
    public void DockerHubFullWidthInputIsFoldedBackToAscii(string typed, string expected) =>
        Assert.Equal(expected, RegistryUsername.Normalize(typed, dockerHub: true));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingNamesNormalizeToEmpty(string? typed) =>
        Assert.Equal(string.Empty, RegistryUsername.Normalize(typed, dockerHub: true));

    [Theory]
    [InlineData("ABC", "abc")]
    [InlineData("Acme-Team", "acme-team")]
    [InlineData("User_Name", "user_name")]
    [InlineData("用户", "用户")]
    [InlineData("a123456789012345678901234567890", "a123456789012345678901234567890")]
    public void NormalizationLeavesUsernameValidationToTheRegistry(string typed, string expected) =>
        Assert.Equal(expected, RegistryUsername.Normalize(typed, dockerHub: true));

    [Theory]
    [InlineData("docker.io", true)]
    [InlineData("registry-1.docker.io", true)]
    [InlineData("index.docker.io", true)]
    [InlineData("https://index.docker.io/v1/", true)]
    [InlineData("https://registry-1.docker.io/v2/", true)]
    [InlineData("http://docker.io", true)]
    [InlineData("https://docker.io:443", true)]
    [InlineData("http://docker.io:80", true)]
    [InlineData("docker.io:443", true)]
    [InlineData("  DOCKER.IO  ", true)]
    [InlineData("DOCKER.IO", true)]
    [InlineData("contoso.azurecr.io", false)]
    [InlineData("ghcr.io", false)]
    [InlineData("docker.io.example.com", false)]
    [InlineData("mydocker.io", false)]
    [InlineData("subdomain.docker.io", false)]
    [InlineData("https://docker.io.example.com/v1/", false)]
    [InlineData("https://registry.example.com/docker.io", false)]
    [InlineData("https://docker.io@registry.example.com", false)]
    [InlineData("https://username@docker.io", false)]
    [InlineData("https://username:password@docker.io", false)]
    [InlineData("https://@docker.io", false)]
    [InlineData("https://docker.io:5000", false)]
    [InlineData("http://docker.io:443", false)]
    [InlineData("docker.io:5000", false)]
    [InlineData("ftp://docker.io", false)]
    [InlineData("https://docker.io?registry=example.com", false)]
    [InlineData("https://docker.io#registry", false)]
    [InlineData("https://docker.io\\@registry.example.com", false)]
    [InlineData("not a host", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DockerHubHostsAreRecognized(string? host, bool expected) =>
        Assert.Equal(expected, RegistryUsername.IsDockerHub(host));
}
