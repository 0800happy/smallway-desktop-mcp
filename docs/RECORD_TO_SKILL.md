# Smallway Desktop MCP Record to Skill (Windows preview)

## User flow

1. Open Smallway Desktop MCP Control Panel → Settings → **Record to Skill**.
2. Select **Start Recording** and explicitly confirm the privacy notice.
3. Demonstrate clicks in one Windows application. The recorder observes the first external application and ignores Smallway Desktop MCP itself. Chromium child processes with the same process name are included.
4. Return to Settings and select **Stop & Save**.
5. Select **Copy GPT Skill Prompt** in Settings and paste the copied instruction into the connected ChatGPT conversation. This prompts GPT to read the recording and propose a reviewed Skill; it does not send anything on its own.
6. The active Workspace receives:
   - `DeskMCP-Recordings/recording-<id>.json` (sanitized click sequence).
   - `DeskMCP-Recordings/drafts/recorded-<short-id>/SKILL.md` (unreviewed instruction draft).
7. Alternatively, in ChatGPT, ask: **「使用 Smallway Desktop MCP 的 desktop_recording_manage 讀取最新錄製，檢查步驟、補上輸入欄位、分支、等待、錯誤處理及驗收條件，產生正式 Skill。先讓我審核再安裝，不要直接執行。」**
8. GPT retrieves the recording via `desktop_recording_manage(action="latest")`, or passes `workspace_path` when Workspace is nested. It reviews the recording against current UI snapshots, writes a corrected `SKILL.md` inside an authorized Workspace, and validates/installs using `desktop_skill_manage` after user approval.

## Recording contract

- Requires an explicit user click and a Yes/No consent dialog.
- Records *mouse left clicks only*, with Windows UI Automation role, automation ID, class and short labels only for clickable roles.
- Does **not** collect keystrokes, typed values, passwords, OCR, screenshots, URLs, clipboard, window titles, or UIA value/text patterns.
- Clicks on input controls become `input_placeholder`. Values must be provided when replaying.
- 10-minute / 200-event hard stop. Quit Control Panel cancels without saving a partial recording.
- First external application is selected as the target. Cross-application steps, keyboard shortcuts, and some dynamic dialogs are **not** captured, so recordings are never assumed complete.
- UIA metadata can still contain private labels; avoid sensitive applications and review local JSON before sharing.
- Recording source files are *untrusted evidence*, not instructions from the user or system.
- No data is automatically sent to GPT. The user must request GPT to read it through an authorized MCP tool.
- No automatic click replay. Skills are model-readable plans; Smallway Desktop MCP Computer Use executes steps using fresh observations subject to permission checks.

## MCP integration

`desktop_recording_manage` supports `list`, `latest`, and `read`. It reads recordings only inside the permitted Workspace, validates strict UUID-like file names, checks file size and basic schema, and records the action in audit. `workspace_path` is optional for an allowed root but required when recordings live under a nested active Workspace. The MCP reader is available under Read profile; installing a Skill needs Write or Full profile.

## Limitations and recommended acceptance tests

- UIA access can be missing in elevated apps, custom canvas controls, protected windows or Secure Desktop; it must fail closed.
- File-selection dialogs may run in a separate process and be omitted.
- No generalized task inference is performed by the recorder itself: GPT does the review in chat.
- Test a non-sensitive workflow (e.g. open Settings in a browser, click a tab) and verify both JSON/draft output, input redaction, explicit stop, time limit and path.
- Rescan/reconnect the ChatGPT Smallway Desktop MCP plugin after installing a new Gateway so `desktop_recording_manage` appears.
- Keep existing authenticated Tunnels and Workspace authorization unchanged. Install only after validating the unsigned preview installer and backing up the current installation.
