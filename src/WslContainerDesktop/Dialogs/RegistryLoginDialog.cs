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
    private bool _foldingCase;

    /// <summary>Gets or sets the username.</summary>
    public string Username { get; private set; } = string.Empty;

    /// <summary>Gets or sets the password.</summary>
    public string Password { get; private set; } = string.Empty;

    /// <summary>Creates a new <c>RegistryLoginDialog</c> and wires the state used by the dialog or model.</summary>
    /// <param name="registryName">The registry name value supplied by the caller.</param>
    /// <param name="prefillUsername">The prefill username value supplied by the caller.</param>
    /// <param name="dockerHub">
    /// True for Docker Hub, whose Docker IDs use lower-case letters and digits.
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
            // Normalize handles upper case and full-width ASCII before validation.
            Text = "A Docker Hub Docker ID is 4 to 30 letters and digits. Upper case is corrected for you; remove anything else, such as Chinese text, spaces or punctuation. If you use an access token, keep the Docker ID here and put the token in the password field.",
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

        _userBox.TextChanged += OnUserTextChanged;
        PrimaryButtonClick += OnPrimary;
    }

    /// <summary>
    /// Makes ASCII case correction visible while typing. Submit-time normalization also covers
    /// prefilled usernames and full-width input.
    /// </summary>
    private void OnUserTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_dockerHub || _foldingCase)
        {
            return;
        }

        var text = _userBox.Text;

        // Plain ASCII only: rewriting text while an IME composition is in flight would corrupt it.
        foreach (var c in text)
        {
            if (c > (char)0x7F)
            {
                return;
            }
        }

        var lowered = text.ToLowerInvariant();
        if (string.Equals(lowered, text, StringComparison.Ordinal))
        {
            return;
        }

        var caret = _userBox.SelectionStart;
        _foldingCase = true;
        try
        {
            _userBox.Text = lowered;
            // Case folding never changes the length, so the caret index still lands in bounds.
            _userBox.SelectionStart = Math.Min(caret, lowered.Length);
        }
        finally
        {
            _foldingCase = false;
        }
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Full-width folding and trimming still happen on submit; the live fold above covers
        // only ASCII case so an IME composition is never rewritten mid-entry.
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
