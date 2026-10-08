# UI localization

The application supports `en-US` and `zh-Hans`. Keep both `Resources.resw` key sets and format
placeholder sets identical. Use full sentences with numbered placeholders; do not translate by
replacing fragments of a message.

For static XAML, give each control a compatible `x:Uid` and matching
`i18n:Localization.Uid` (`xmlns:i18n="using:WslContainerDesktop.Helpers"`). The attached property
updates the existing control on language changes. Share a UID only between the same control type.
Attached-property resource names use WinUI's fully qualified syntax, for example
`ButtonHelp.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip`.

Use `UiText.Get(key, englishFallback, args)` for generated display text. Subscribe to
`UiText.LanguageChanged` to refresh computed labels or one-time bindings; release page event
subscriptions when navigation leaves a page. `UiText.Translate` projects a complete known
application message, and `TranslateLines` supports known guidance lines. Unknown text is retained.

Keep protocol values separate from labels. Never translate commands, paths, IDs, resource names,
enum values, JSON/YAML, raw logs, user templates, imported descriptions or assistant conversation
content. Translate built-in template display fields only. Preserve established technical/product
names including WSL, Docker, Compose, Kubernetes, Ollama, API, JSON, YAML and Dev Container.
Picker items use stable values and observable labels so changing a label cannot clear selection.

The stored empty language preference means follow Windows. Resolve it through
`SystemUiLanguages.Get()` and `AppLanguage.Resolve` before using MRT, a resource context or
`FrameworkElement.Language`. Never pass the empty preference to those APIs. Read Windows' UI
preferences independently of `ApplicationLanguages`, which includes the persisted app override.

Run `I18nResourceTests`, language-switch tests, the application build and a signed isolated MSIX
smoke test. Check collapsed pickers, explicit-to-system language changes, relaunch, cached pages,
unfinished inputs and nonmutating dialog opens. Preserve the primary checkout and official package.
