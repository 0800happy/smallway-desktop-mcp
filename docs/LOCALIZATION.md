# Smallway Desktop MCP — Windows Localization

**Supported:** `zh-TW` (default), `en-US`. 2026-10-20 implementation for WPF Control Panel and standalone Windows Setup.

## User experience

Go to **Settings → Language / 語言**, select **繁體中文** or **English**. The visible WPF text updates immediately, including dynamically changed Gateway/Tunnel status labels and newly opened WPF dialogs. The two locale buttons show the current selection. No Gateway restart is required.

The preference is persisted in existing `settings.json` as `uiLanguage` and as `ui-language.txt` under the already authorized `DESKTOP_MCP_SETTINGS_DIR` (by default `%APPDATA%\DesktopMCP`). The installer reads and, on an actual GUI install, saves that language preference so first launch uses the selected language; isolated UI image capture does not change it.

Existing installs without a language setting default to Traditional Chinese. Unknown language values fall back to `zh-TW`; switching to English retains original English source strings. No changes to existing Workspace, Tunnel, permission, MCP protocol, APIs or source-sensitive file names.

## Resource format and maintenance

- `control-panel/wpf/locales/zh-TW.json`: keyed by existing English display strings, with Traditional Chinese translations.
- `control-panel/wpf/locales/en-US.json`: English baseline; English UI text remains unchanged.
- `control-panel/wpf/UiLocalization.cs`: loads local resources, translates WPF `TextBlock.Text`, `ContentControl.Content`, `HeaderedContentControl.Header`, tooltip text, window titles, and restores English strings when switching languages. It does **not** translate editable `TextBox`, passwords, security keys, application protocols or arbitrary filesystem paths.
- `control-panel/wpf/Panel.xaml`: `LanguageCard` with two locale buttons.
- `installer/DeskMCPInstaller.cs`: compact standalone Windows Forms locale dictionary and language picker. The uninstaller/OS-generated error descriptions may still use English and should be translated separately if expanded.

For new UI strings, add the English text as a translation key and provide `zh-TW` text. Keep `Gateway`, `Tunnel`, `Workspace`, `SSH`, `API`, `MCP`, `Token` and other protocol terms in English when appropriate. Do not localize literal API fields, file names, regex contracts or lock / session policy messages that tests depend upon.

This is UI translation, not a machine translation of arbitrary exception text. Unknown future status messages, raw server errors and some legacy uninstaller dialogs can remain English until their strings are added to the reviewed catalog. No third-party web translation service is used.

## Isolated verification

1. Compile Windows WPF Release. `--localization-self-test` should return exit code 0 after zh-TW → en-US → zh-TW status / restoration checks.
2. Generate previews with `SMALLWAY_PREVIEW_LOCALE=zh-TW` or `en-US` and `--capture-settings`, `--capture-first-run`, `--capture-full-modal`. The preview must use an isolated `DESKTOP_MCP_DATA_ROOT` and cannot start the production Gateway.
3. Run `npm test`, version consistency and user-facing tool contract checks (29 tools).
4. Build the exact committed installer in a short-path isolated checkout; require stage + installer smoke tests, dependency notices, secret scan, manifest and SHA-256 verification.
5. After deployment approval, manually switch languages and reopen Settings to verify persistence. Check high-risk confirmation text, Chinese glyph width and mixed English technical terms.

This independent Smallway fork retains DeskMCP internal process names, Startup shortcut identity, installation state paths and local MCP API semantics for in-place upgrade compatibility.
