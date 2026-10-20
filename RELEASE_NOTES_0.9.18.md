# Smallway Desktop MCP 0.9.18 — Release Notes

Release date: 2026-10-20 (source candidate; binary release pending final verification).

## Overview

Smallway Desktop MCP is an independently maintained adaptation of [edmen12/DeskMCP](https://github.com/edmen12/deskmcp), distributed under the Apache License 2.0. It is not an official release by, or endorsed by, the original author.

## Changes

- Adopt **Smallway Desktop MCP** as the visible product and installer branding.
- Support Traditional Chinese (`zh-TW`, default) and English (`en-US`) in the Windows Control Panel and standalone installer.
- Add **Settings → Language / 語言** for immediate language switching with a persisted preference.
- Translate visible Control Panel text, status labels, common dialogs, and installer controls; retain protocol terms and identifiers where required.
- Add a Traditional Chinese README, localization guide, and illustrative screenshots.
- Retain existing `DeskMCP` internal executable/process, `DesktopMCP` data directories, startup identity, Workspace/Tunnel settings, and MCP tool compatibility for safer upgrades.
- Point fork-specific update and distribution references to `0800happy/smallway-desktop-mcp`, not the upstream project.

## Compatibility and limitations

- Upgrades must preserve existing user settings; back up your current installation and `%APPDATA%\DesktopMCP` and `%LOCALAPPDATA%\DesktopMCP` before replacing a running installation.
- Do **not** run this fork and the original DeskMCP simultaneously against the same user-data directory.
- Certain platform-generated errors, third-party messages and legacy uninstaller dialogs may remain in English.
- Windows binaries are **unsigned** unless a published artifact independently passes Authenticode verification. Do not imply that upstream code-signing approval applies to this fork.
- Third-party license/redistribution review and clean-user installation and live-upgrade verification remain necessary before a public binary release.

## Source, licenses and verification

- Repository: https://github.com/0800happy/smallway-desktop-mcp
- Upstream attribution: `docs/FORK_ATTRIBUTION.md`
- License and notices: `LICENSE`, `NOTICE`, `THIRD_PARTY_NOTICES.md`
- Localization details: `docs/LOCALIZATION.md`
- The final Windows installer must be built from an exact clean source commit and pass `scripts/build-installer.ps1`, `scripts/check-release-readiness.ps1`, release-stage and installer smoke tests, secret scan, and SHA-256/manifest provenance checks before a GitHub Release is published.

Publishing source code is separate from publishing a verified Windows installation package.
