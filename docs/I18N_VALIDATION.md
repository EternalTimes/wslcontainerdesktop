# Localization implementation and validation — 2026-10-09

Changes are isolated on `codex/complete-chinese-ui`, based on `391c58d066e364c4b57083585fa3fed5f3eacec0`.
The primary checkout remains clean at that commit. The shipping package (`393193CD-4A5B-4502-BC94-7C6AF142CD28`,
version 2.2.0.0) and existing `WslContainerDesktop.TestBuild` 2.2.10.0 were not replaced.
Implementation commits are unsigned; the isolated MSIX below is signed.

## Review findings and fixes

The MiniMax handoff includes the previous Settings UID/live-switch work (`4378342`, `93bcef9`,
merged through `050002e`) and `391c58d`, which prevents an empty language preference from being
written to WinRT language properties. Those changes are preserved. Git records the shared
Eternal Times author, so the commit author alone does not identify the coding agent.

The remaining system-language bug came from consulting application languages containing the
persisted application override. `SystemUiLanguages` now reads independent Windows UI preferences;
`AppLanguage.Resolve` always returns a supported nonempty effective language before MRT,
resource-context or root-element writes. The empty stored preference still means follow Windows.
Unsupported/invalid preferences resolve safely. Window activation refreshes the system selection.

Replacing string picker items during translation could temporarily clear selection and send `-1`
into setters. Stable observable picker items and validated setters now preserve the language and
theme selections, including the collapsed label. Language changes update existing controls and
cached pages instead of rebuilding the page or discarding input/state.

Both locales now contain **3497 identical resource keys** (3369 added to the original 128).
Static labels, help, tooltips, accessibility labels, dialogs, status/progress text, built-in templates,
Compose/Dev Container explanations, engine/storage pages and tray text use localized presentation.
WSL, Docker, Compose, Kubernetes, Ollama, API, JSON, YAML and other established technical/product
names retain their canonical spelling. Commands, paths, IDs, protocol values, raw logs/errors,
user/imported templates and assistant conversation content remain intact. Computed localized
model fields are excluded from JSON evidence. Unknown output is preserved by the message matcher.

Additional bugs found and fixed during validation:

- Compose preview counted downloads using `Contains("Pull")`, although its real projection says
  `Downloads this image…`. Eight projection-to-view-model regression cases verify counts across
  both languages, replicas, cached images/builds and create/recreate plans.
- Position-only message templates could consume unknown error text; only templates containing
  literal letters are registered. Exact known messages and complete templates are translated.
- Windows PRI names are case insensitive; a case-only duplicate resource key was renamed and
  is now rejected by a regression guard.
- Actual UI inspection found the network summary mixing `built-in` with Chinese. Its display
  getter now formats complete sentences from typed counts.
- Static property inspection found two untranslated InfoBar messages, two Refresh buttons and
  four accessibility names. They have independent compatible UIDs and complete resources.

## Automated validation

Final complete suite, `dotnet test` for both target frameworks:

| Framework | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| net10.0 | 2294 | 0 | 3 |
| net10.0-windows10.0.26100.0 | 3316 | 0 | 16 |
| Total | **5610** | **0** | **19** |

The 19 skips are existing opt-in engine/runtime/Foundry scenarios. They were not enabled for this
localization validation. Final focused resource/language/Compose tests passed 40 cases per framework.
The final full run used the assemblies compiled by that focused run (`--no-build --no-restore`).
TRX results are saved beside the test package in its `validation` subdirectory,
with the `final-i18n` prefix; generated results were removed from the worktree.

Resource guards cover locale parity, exact and case-insensitive duplicates, format arguments,
explicit lookup keys, control-type compatibility, attached-property names and live UID mirrors.
A new guard checks every static English text property in Views/Dialogs, including Message,
Description and accessibility names, while exempting exact technical labels, product names,
URLs and bindings. It covers the two InfoBar omissions found above.

The x64 Release application, compiled PRI resources and isolated MSIX built with **0 warnings
and 0 errors**. Signature verification also reported **0 warnings and 0 errors**. The installed
SDK is 10.0.401; only temporary validation/build copies allowed that patch because the committed
10.0.400 SDK is absent. The repository SDK policy was restored afterward.

NuGet's official vulnerability index pointed to unavailable (HTTP 404) data; an alternate official
audit endpoint also failed. Vulnerability auditing was disabled only in the temporary test-package
build invocation to isolate that external failure. No repository audit policy or package versions
were changed. **Dependency vulnerability auditing remains unverified.**

## Actual GUI coverage and limits

The full translation build was inspected using the computer-use skill on real WinUI windows.
Chinese navigation and representative contents were checked in Dashboard, Containers, Images,
Volumes, Networks, Endpoints, Activity, Registries, Compose, Templates, Kubernetes, WSL Engine,
Disk Usage and Settings. Container summary/log controls and five nonmutating dialog opens
(Run Container, Pull Image, Create Volume, Create Network, New Template) were inspected and
cancelled. Technical identifiers remained intact. Collapsed Settings language/theme labels were
visible, and the Settings page was inspected through its lower sections.

An earlier package containing the same core language fix passed English → follow Windows and
returned to Chinese. Final **2.2.13.0** cold startup with stored `Language=""` displayed Chinese
and retained Dark theme; its actual network summary displayed `3 个内置网络`. Startup logs recorded
effective `zh-Hans` and contained no new application errors during those checks.

The computer-use helper then timed out on the Compose navigation click and state capture, and
remained busy after retry/reset. Therefore final 2.2.13 Compose-message visual verification,
English → follow Windows, state retention across switching and a subsequent relaunch were
**not completed**. Automated resource/language guards passed; they do not substitute for these
remaining GUI checks. Authenticated registry/AI flows, installed-cluster Kubernetes details,
Dev Containers runtime and the remaining dialogs were not exercised. Existing containers, images,
volumes, networks and engine settings were not changed by the GUI checks.

## Test artifact

- Identity: `WslContainerDesktop.I18nVerify`, version **2.2.13.0**, installed beside the shipping app.
- File: `WslContainerDesktop.TestBuild_2.2.13.0_x64.msix` (110.8 MB).
- SHA-256: `AEBF006796C9866E87A72D24F753FBB10327BA0E07F9F5FA21AB1DEA9756D6DA`.
- Signature: trusted local test certificate `CN=Michael Hacker`, expires 2026-10-21; no timestamp.
- Output directory: `%LOCALAPPDATA%/WslContainerDesktop/I18nVerify/out`.

Localization maintenance rules are in [LOCALIZATION.md](LOCALIZATION.md).
