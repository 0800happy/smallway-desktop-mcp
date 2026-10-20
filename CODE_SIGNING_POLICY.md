# Smallway Desktop MCP signing policy

This independent Apache-2.0 fork is based on DeskMCP by edmen12 (https://github.com/edmen12/deskmcp).
Original author SignPath Foundation applications, credentials, signatures and signing
policies do not apply to Smallway Desktop MCP.

Our release builds are unsigned unless a separate Smallway signing identity has
been approved and independently verified. Never describe an unsigned build as signed.
Verify source commit, SHA-256, dependency licenses, secret hygiene, installer and
rollback tests before publishing. Keep signing secrets outside source control.
