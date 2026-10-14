# Settings: inline Authorized folders (development update)

Feature branch: feature/workspace-allowed-folders-inline

## Change
- The Settings > Workspace card directly shows all authorized folders, including both the primary Workspace and every additional allowed folder, with a count.
- Rows label PRIMARY / ADDITIONAL and wrap long Windows paths instead of hiding them.
- If a configured directory is unavailable, the row stays visible and shows "Folder currently unavailable" without automatically deleting its setting.
- The original Workspace Change control and the Manage additional folders dialog remain available.
- The inline list refreshes after changing the primary folder, adding/removing an additional folder, and changing light/dark theme.
- Recent Workspaces remains a separate list of previously selected primary directories; it is not the authorized-folder list.

## Verification
- WPF build: success, 0 warnings, 0 errors.
- --multi-channel-self-test: Exit 0.
- --tunnel-status-self-test: Exit 0.
- --runtime-reliability-self-test: Exit 0.
- git diff --check: success.

## Deployment
- This changes only the Control Panel's presentation; Gateway authorization logic is unchanged.
- The currently installed DeskMCP should be kept running until the newly built installer has been verified and a controlled update is authorized.
- All connected Tunnels share one Gateway and the same combined Workspace access.
