# DeskMCP Project Connections V1.0 (Windows preview)

> 2026-10-20 — This feature is a **user-initiated Codex handoff aid**, not a scheduler, agent fallback daemon, or way to bypass platform security reviews. It remains an **uninstalled development preview** until explicitly approved for deployment.

## Scope

- Keep SSH passwords, private-key **paths**, Cloudflare tokens and WordPress application passwords in the user's existing Markdown files. Markdown formats may differ; local parsing supports simple English/Chinese key-value pairs and Markdown tables.
- Scan only a **manually selected project folder** (up to three levels, 150 candidate MD files, max 256 KiB per MD). Skip backup, Git and dependency directories; do not follow symlinks or junctions.
- The local DeskMCP Settings → **Project Connections → Manage** screen allows selecting an MD source and explicitly approving the service kind, host, username, section and secret field name. For SSH a verified SHA-256 host-key fingerprint must be supplied out of band.
- The three ChatGPT Tunnel clients share the same Gateway and locally approved connection registry. This is not per-account isolation.
- Local registry: dynamically derived `%LOCALAPPDATA%/DesktopMCP/project-connections/registry.json`. It stores pointers/metadata **not secret contents**. The MD remains the sole credential source.
- A new tool, `desktop_project_connections`, exposes `scan`, `list`, `ssh_read`, `api_get`, `request_ssh`, `request_api` and `execute`. `ssh_read` allows only a small predeclared diagnostic-command allowlist; API GET calls are limited to Cloudflare v4 or WordPress v2 endpoints.
- Other SSH commands and API writes create a 15-minute pending approval. The user must review the exact request in the **local control panel**, explicitly approve it and then execute it once. Approval is consumed before network I/O.
- Both credential resolution and SSH/API authorization headers remain local to the Gateway. No private key, password, token or Authorization header is deliberately returned by the MCP tool; response redaction and bounded output are defense in depth.

## User workflow

1. The user explicitly asks ChatGPT to resume a named Codex thread/task. Preserve existing schedules and read applicable AGENTS.md / HANDOFF.md / project rules first.
2. Locally open DeskMCP → Settings → Project Connections → Manage.
3. Choose the **specific project folder** and scan candidates. Select one MD; enter the approved host, account and precise MD section/field name when needed; never copy the password to ChatGPT.
4. For SSH, independently verify the server fingerprint before enrollment. Reject unexpected host-key changes.
5. Select **Approve selected MD connection** locally. If multiple fields match, the Gateway fails closed and requires a clearer section/field mapping.
6. Use the connection ID with the new MCP tool; this is not a privilege bypass. Read-only diagnostics can run with an approved entry in session-only Full Control. Non-read-only operations require a separate local approval per request.
7. Check the remote result; for an ambiguous write outcome, verify the remote state before trying again. Record the verified result in the project's HANDOFF.md.

## Security limits (important)

- Markdown **still contains plaintext secrets**, so protect local NTFS ACLs, Google Drive shares, backups and repository exclusions. Do not commit these MD files publicly.
- Existing general-purpose Full Control process tools operate under the user's Windows identity; Project Connections approvals do **not** provide OS-level isolation from all other local programs.
- The source file is re-read before each operation. Secret rotation works when the pinned host, username and approved field still match. If source/identity changes or ambiguity appears, operations stop for reapproval.
- SSH uses the `ssh2` library and requires a verified pinned server public-key fingerprint. SSH-key mode accepts a local key-file path restricted to the approved project or the dynamically resolved Home; V1.0 does not support encrypted private-key passphrases.
- Never pass passwords in the command string, API URL, request body or tool arguments. Do not use this feature to send a token to a different host.
- Remote API responses, SSH stdout/stderr, and user-created request bodies can contain sensitive values not known beforehand; redact carefully and avoid requesting data dumps.
- Full Control is session-scoped. The tool may still be refused by a ChatGPT platform safety check; **modifying DeskMCP cannot lift such a platform restriction**. Treat that result as BLOCKED; do not reroute a prohibited action.
- This preview adds **no Windows Task Scheduler jobs, no Codex automation edits, no heartbeat monitoring and no unattended takeover**.

## Verification (synthetic inputs only)

- Node / TypeScript tests include Markdown field parsing, ambiguity handling, credential rotation, path-scoped scan, registry non-disclosure and redaction.
- Compile the WPF control panel and test the settings UI in an isolated instance. Do not install over a running production DeskMCP while testing.
- Check package audit, production license inventory, the Gateway tool contract and installer smoke before distributing.
- Network login to a real SSH server or API is **not** part of automated tests; it requires a separate user-approved acceptance test after deployment.
