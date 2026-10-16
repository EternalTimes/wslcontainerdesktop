# Release signing

The release workflow (`.github/workflows/release.yml`) builds a **self-signed, self-contained
MSIX** and publishes it as a GitHub Release. Signing uses two repository secrets:

| Secret | Contents |
|--------|----------|
| `PFX_BASE64` | Base64 of a PFX containing the code-signing certificate and its private key. The certificate subject **must** match the package manifest `Publisher` (`CN=Michael Hacker`). |
| `PFX_PASSWORD` | The password protecting that PFX. |

These are already set for this repository. The workflow decodes the PFX, builds the MSIX
unsigned, then signs it with `signtool` (from the `Microsoft.Windows.SDK.BuildTools` NuGet
package). It also exports the public `.cer` from the PFX and attaches it to the release so end
users can trust the publisher.

## Release installer helper

The workflow stages `Install.ps1` with the resolved release version, repository, and SHA-256 of
both the exported certificate and signed MSIX using `Stage-InstallScript.ps1`. The published helper downloads only
missing assets from that exact release and verifies both hashes and the package identity before trusting the certificate. Matching
local assets are reused for offline installation; conflicting certificates are rejected, not replaced.
The hashes bind the assets to the downloaded script, not to an independent trust authority.
Only captured, verified certificate bytes are sent to a fixed elevated import command; the helper
never reruns a file from the download folder elevated. The verified package is held read-only,
denying writes and replacement until installation finishes. Windows enforces the MSIX signature
during deployment. The unstamped source helper refuses installation and certificate trust because
it has no pinned asset hashes. Use the helper attached to a release, including for offline installs.

Stable releases newer than the current latest stable version are explicitly marked latest;
older maintenance releases and prereleases are not. The README's
`releases/latest/download/Install.ps1` link therefore provides the latest stable helper. Saved
helpers remain pinned to their original release. Release runs share a concurrency group so
version comparison and publication cannot race across branches.

This behavior starts with the first release containing the updated helper. Scripts already attached
to older releases still need both assets downloaded manually. No signing certificate or package
identity change is involved.

Installer checks use only built-in PowerShell/.NET functionality and mock downloads, elevation,
certificate import, and deployment. Run these from the repository root; the release workflow runs
both before building:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Install.Tests.ps1
pwsh -NoProfile -File .\tests\Install.Tests.ps1
```

## Why the same certificate matters

Windows treats an MSIX update as the *same* app only when the new package has the **same
publisher identity** (and a higher version). Re-using one stored certificate across every
release means users trust the certificate once and can update in place. Generating a fresh
certificate per build would force users to re-trust and reinstall each time — which is why the
certificate lives in secrets rather than being created on the fly.

## Rotating / recreating the certificate

> [!WARNING]
> **Rotating the certificate ends automatic updates for every existing installation.** The in-app
> updater installs a release only if it is signed with the *same* certificate (compared by SHA-256
> thumbprint) as the copy already installed, and Windows only accepts a self-signed package whose
> exact certificate the user has trusted. A release signed with a new certificate therefore shows
> *"This release is signed with a different certificate…"* in the app, and every user has to
> download it and trust the new `.cer` by hand, as for a first install. Only rotate when you have
> to (compromise or imminent expiry — the current certificate is valid for ten years), and say so
> prominently in that release's notes and `CHANGELOG.md`.

Run locally (PowerShell), then update the two secrets:

```powershell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Michael Hacker" `
  -KeyUsage DigitalSignature -FriendlyName "WSL Container Desktop Signing" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}") `
  -NotAfter (Get-Date).AddYears(10)

$pw = ConvertTo-SecureString "<a-strong-password>" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath .\sign.pfx -Password $pw
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)"   # keep only the .pfx file

$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes(".\sign.pfx"))
$b64 | gh secret set PFX_BASE64 --repo mhackermsft/wslcontainerdesktop
"<a-strong-password>" | gh secret set PFX_PASSWORD --repo mhackermsft/wslcontainerdesktop
```

> The `Publisher` in `src/WslContainerDesktop/Package.appxmanifest` must equal the certificate
> subject. If you change one, change the other.

## Upgrading to a real certificate

To remove the "unknown publisher / trust this certificate" step for users, replace the
self-signed certificate with one from a trusted CA (or an Azure Trusted Signing account) and
update the two secrets plus the manifest `Publisher` to match the new subject. No workflow
changes are required. A new `Publisher` makes it a different package to Windows, so — as with a
rotation — existing installations cannot update in place and users must install the new release
manually (and uninstall the old one).
