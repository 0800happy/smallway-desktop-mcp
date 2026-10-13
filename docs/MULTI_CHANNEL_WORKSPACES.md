# DeskMCP: three channels + multiple Workspace folders (development branch)

Branch: feature/multiple-tunnels-workspaces
Source: https://github.com/edmen12/deskmcp (Apache-2.0)
Status: development build; NOT installed over the existing DeskMCP release.

## User-visible additions

In Settings:
* Tunnel -> "Manage Channels (up to 3)".
  * Channel 1 = original DeskMCP Tunnel, still configured by the existing Configure / Reconnect controls.
  * Channels 2 and 3 = independently named entries with Tunnel ID, encrypted runtime API key, per-channel start/stop, auto-start and removal.
  * Additional channels share the SAME local Gateway and tool permissions.
  * Each additional Tunnel receives an ephemeral loopback health port and separate PID, URL and log files.
* Workspace -> "Manage additional folders".
  * Allow several specific C:/D:/G: folders together.
  * The original primary workspace stays selected through the old Change button.
  * Adding/removing folders restarts the Gateway (temporarily interrupts all channels).
  * The Gateway already supports semicolon-separated DESKTOP_MCP_ALLOWED_ROOTS and canonicalizes every allowed root.

## Credentials and safety

* Additional keys are encrypted with Windows DPAPI CurrentUser and stored in:
  %LOCALAPPDATA%\DesktopMCP\additional-tunnels\<guid>.key.dpapi
* Settings.json contains names, Tunnel IDs, and auto-start flags, NOT API keys.
* Only processes started and retained by the running Control Panel are stopped.
* Detached tunnels are never force-killed by guessing based on tunnel-client.exe.
* Existing BAT-managed Tunnel #2 must be manually stopped before starting the SAME ID inside the new manager; otherwise two clients may attempt to connect the same tunnel.
* The first/original channel is intentionally not auto-imported into an additional slot.
* Individual Tunnel processes do not have separate filesystem permissions; all share one Gateway and its union of authorized folders.

## How to build without modifying the installed DeskMCP

On the Changhua Windows laptop, .NET SDK 10.0.401 has been locally installed under:
%LOCALAPPDATA%\DeskMCP-Dev-SDK

Because source is inside a Google Drive synced G: folder, direct EXE builds can fail due to CreateAppHost file locks. Copy the source to a local scratch build folder, EXCLUDING .git/bin/obj/node_modules/runtime, then build there:

    %LOCALAPPDATA%\DeskMCP-Dev-SDK\dotnet.exe build control-panel\wpf\DeskMCP.ControlPanel.csproj -c Release --nologo

The build does not install or run the new panel. Do not replace a running production DeskMCP executable.

Non-destructive tests:

    DeskMCP.exe --runtime-reliability-self-test
    DeskMCP.exe --tunnel-status-self-test
    DeskMCP.exe --multi-channel-self-test

## Still required before production adoption

* Compile and run the new multi-channel self-test after any source change.
* UI review on an isolated DeskMCP development instance and manual independent start/stop testing for all three channels.
* Confirm safe migration of the second BAT-managed tunnel, including removal of its Windows Startup shortcut to prevent duplicate activation.
* Verify OpenAI org/tunnel credentials for each account, recovery, key rotation, app updates and uninstall.
* Run full install/build-stage regression checks.
