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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using WslContainerDesktop.Helpers;

namespace WslContainerDesktop.Dialogs;

/// <summary>Collects username + password to log in to an already-registered registry.</summary>
public sealed class RegistryLoginDialog : ContentDialog
{
    private readonly TextBox _userBox;
    private readonly PasswordBox _passwordBox;
    private readonly TextBlock _userError;
    private readonly bool _dockerHub;

    /// <summary>Gets or sets the username.</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>Gets or sets the password.</summary>
    public string Password { get; private set; } = string.Empty;

    /// <summary>Creates a new <c>RegistryLoginDialog</c> and wires the state used by the dialog or model.</summary>
    /// <param name="registryName">The registry name value supplied by the caller.</param>
    /// <param name="prefillUsername">The prefill username value supplied by the caller.</param>
    /// <param name="dockerHub">
    /// True for the built-in Docker Hub entry, whose usernames are lower-case only. The typed name
    /// is normalized for it, because Docker Hub reports an unusable name as a malformed
    /// Authorization header instead of as a wrong password.
    /// </param>
    public RegistryLoginDialog(string registryName, string? prefillUsername, bool dockerHub = false)
    {
        _dockerHub = dockerHub;
        Title = $"Log in to {registryName}";
        PrimaryButtonText = "Log in";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _userBox = new TextBox
        {
            Header = "Username",
            PlaceholderText = "username or token name",
            Text = prefillUsername ?? string.Empty,
            MinWidth = 360,
        };

        _userError = new TextBlock
        {
            // Lower case and full-width input are already folded in by Normalize, so this only
            // ever appears for a name Docker Hub could never have issued.
            Text = "A Docker Hub username is 4 to 30 lower-case letters and digits, with no other characters. Enter your Docker ID exactly as Docker Hub shows it; if you use an access token, keep the Docker ID here and put the token in the password field.",
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed),
        };

        _passwordBox = new PasswordBox
        {
            Header = "Password / token",
        };

        Content = new StackPanel
        {
            Spacing = 10,
            Children = { _userBox, _userError, _passwordBox },
        };

        PrimaryButtonClick += OnPrimary;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Docker Hub only accepts a lower-case account name, and reports anything else as a
        // malformed Authorization header. Fold the typed name here so the login can succeed,
        // and show the result so the correction is visible rather than silent.
        var normalized = RegistryUsername.Normalize(_userBox.Text, _dockerHub);
        if (!string.Equals(normalized, _userBox.Text, StringComparison.Ordinal))
        {
            _userBox.Text = normalized;
        }

        Username = normalized;
        Password = _passwordBox.Password;

        if (!RegistryUsername.IsSupportedByDockerHub(Username, _dockerHub))
        {
            args.Cancel = true;
            _userError.Visibility = Visibility.Visible;
            _userBox.Focus(FocusState.Programmatic);
            return;
        }

        _userError.Visibility = Visibility.Collapsed;

        if (string.IsNullOrEmpty(Password))
        {
            args.Cancel = true;
            _passwordBox.Focus(FocusState.Programmatic);
        }
    }
}
