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

using Microsoft.Extensions.Logging.Abstractions;
using WslContainerDesktop.Services;
using Xunit;

namespace WslContainerDesktop.Tests.Services;

/// <summary>Exercises login metadata with synthetic Win32 results, without accessing real credentials.</summary>
public sealed class RegistryCredentialStoreTests
{
    [Theory]
    [InlineData(1168, RegistryCredentialStatus.Missing)]
    [InlineData(5, RegistryCredentialStatus.Unavailable)]
    [InlineData(1312, RegistryCredentialStatus.Unavailable)]
    [InlineData(-1, RegistryCredentialStatus.Unavailable)]
    public void OnlyNotFoundConfirmsAnAbsentCredential(int error, RegistryCredentialStatus expected)
    {
        var store = Store(_ => (error, null));

        Assert.Equal(expected, store.GetLoginStatus("private.example.com", out var username));
        Assert.Null(username);
        Assert.False(store.IsLoggedIn("private.example.com", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("synthetic-user")]
    public void AnExistingCredentialIsPresentEvenWithoutAUsername(string? storedUsername)
    {
        var store = Store(_ => (0, storedUsername));

        Assert.Equal(RegistryCredentialStatus.Present, store.GetLoginStatus("private.example.com", out var username));
        Assert.Equal(storedUsername, username);
        Assert.True(store.IsLoggedIn("private.example.com", out username));
        Assert.Equal(storedUsername, username);
    }

    [Fact]
    public void DockerHubMustCheckEveryAliasBeforeReportingMissing()
    {
        var checkedTargets = new List<string>();
        var store = Store(target =>
        {
            checkedTargets.Add(target);
            return (1168, null);
        });

        Assert.Equal(RegistryCredentialStatus.Missing, store.GetLoginStatus("docker.io", out _));
        Assert.Equal([
            "wslc-credential/docker.io", "wslc-credential/index.docker.io",
            "wslc-credential/registry-1.docker.io", "wslc-credential/https://index.docker.io/v1/",
        ], checkedTargets);
    }

    [Fact]
    public void OneUnreadableAliasPreventsReportingMissing()
    {
        var store = Store(target => target.EndsWith("registry-1.docker.io", StringComparison.Ordinal)
            ? (5, null) : (1168, null));

        Assert.Equal(RegistryCredentialStatus.Unavailable, store.GetLoginStatus("docker.io", out _));
    }

    [Fact]
    public void APresentAliasStillEstablishesLoginAfterAnotherAliasCannotBeRead()
    {
        var store = Store(target => target.EndsWith("registry-1.docker.io", StringComparison.Ordinal)
            ? (0, "synthetic-user") : (5, null));

        Assert.Equal(RegistryCredentialStatus.Present, store.GetLoginStatus("docker.io", out var username));
        Assert.Equal("synthetic-user", username);
    }

    [Theory]
    [InlineData("mydocker.io")]
    [InlineData("docker.io.example.com")]
    [InlineData("docker.io:5000")]
    public void OtherRegistriesNeverProbeDockerHubAliases(string server)
    {
        var checkedTargets = new List<string>();
        var store = Store(target =>
        {
            checkedTargets.Add(target);
            return target == "wslc-credential/registry-1.docker.io" ? (0, "hub-user") : (1168, null);
        });

        Assert.Equal(RegistryCredentialStatus.Missing, store.GetLoginStatus(server, out _));
        Assert.Equal(["wslc-credential/" + server], checkedTargets);
    }

    [Theory]
    [InlineData("https://docker.io:443/v2", "docker.io:443")]
    [InlineData("https://DOCKER.IO:443/v2", "DOCKER.IO:443")]
    [InlineData("https://registry-1.docker.io:443/v2", "registry-1.docker.io:443")]
    [InlineData("https://private.example.com:5443/v2", "private.example.com:5443")]
    [InlineData("private.example.com:5443/v2", "private.example.com:5443")]
    public void UriCredentialsAreFoundAtTheEnginesHostAndPortKey(string server, string engineKey)
    {
        var checkedTargets = new List<string>();
        var store = Store(target =>
        {
            checkedTargets.Add(target);
            return target == "wslc-credential/" + engineKey ? (0, "synthetic-user") : (1168, null);
        });

        Assert.Equal(RegistryCredentialStatus.Present, store.GetLoginStatus(server, out var username));
        Assert.Equal("synthetic-user", username);
        Assert.Equal(["wslc-credential/" + server, "wslc-credential/" + engineKey], checkedTargets);
    }

    [Fact]
    public void PrivateUrisProbeTheirOwnOriginalAndEngineKeysOnly()
    {
        const string server = "https://docker.io.example.com:443/v2";
        var checkedTargets = new List<string>();
        var store = Store(target =>
        {
            checkedTargets.Add(target);
            return target == "wslc-credential/registry-1.docker.io" ? (0, "hub-user") : (1168, null);
        });

        Assert.Equal(RegistryCredentialStatus.Missing, store.GetLoginStatus(server, out _));
        Assert.Equal([
            "wslc-credential/" + server, "wslc-credential/docker.io.example.com:443",
        ], checkedTargets);
    }

    [Fact]
    public void EmptyServerDoesNotReadCredentials()
    {
        var store = Store(_ => throw new InvalidOperationException("Unexpected metadata read."));

        Assert.Equal(RegistryCredentialStatus.Missing, store.GetLoginStatus("  ", out var username));
        Assert.Null(username);
    }

    private static RegistryCredentialStore Store(Func<string, (int, string?)> read) =>
        new(NullLogger<RegistryCredentialStore>.Instance, read);
}
