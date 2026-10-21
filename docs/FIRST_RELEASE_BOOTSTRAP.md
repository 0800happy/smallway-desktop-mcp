# First public release bootstrap

The normal `Publish verified release` workflow requires a published previous version and a real live-upgrade attestation. The very first public release cannot satisfy that prerequisite. A separate, strictly limited **initial_release** gate exists for a repository with **no published releases**.

## Before first publication

1. Review and merge the approved source changes. For the audited six-dependency update, review [PR #13](https://github.com/0800happy/smallway-desktop-mcp/pull/13), including the successful Windows x64/ARM64 installer validation and macOS ARM64 core-stage checks. Keep Desktop Commander 0.3.0 blocked until its telemetry-contract license is resolved.
2. Verify upstream attribution, third-party licenses, source version, unsigned installer distribution warnings, and release notes. Do not infer a code-signing signature from successful unsigned smoke tests.
3. Create the intended version tag at the **exact approved source commit**, run the **Unsigned release candidate** workflow on that tag or commit, and confirm both x64/ARM64 candidate jobs are successful. Preserve the candidate run ID.
4. Create a **draft, non-prerelease** GitHub Release for that exact tag. Do not publish the draft yet.
5. Manually verify the intended first-install experience as appropriate. If a clean-machine install is unverified, keep the Release as a draft until validated.

## First release only — manual Publish verified release inputs

- `tag`: exact intended tag, for example `v0.9.18`.
- `candidate_run_id`: successful, **exact-source** unsigned release candidate run ID.
- `initial_release`: **true** (must be explicitly enabled; defaults to false).
- `bootstrap_confirmation`: exactly `I_CONFIRM_FIRST_RELEASE_v0.9.18` if the tag is `v0.9.18`; replace the tag text for other versions.
- `previous_version`: leave blank.
- `live_upgrade_attestation`: leave blank.

The workflow serializes publication for the entire repository to prevent competing first-release or Latest updates. It enumerates **all** GitHub Releases, including paginated history, and rejects this mode if **any nondraft release** already exists. It also requires a draft, non-prerelease target Release and retains the same exact-source candidate-run, manifest, hash, installer smoke, asset integrity, and upload checks as normal publication. The workflow publishes the target only after those checks succeed.

## Every subsequent release

Set `initial_release=false` and leave `bootstrap_confirmation` blank. Provide the real `previous_version` equal to the repository's current published Latest release, plus the exact `DESKMCP_LIVE_UPGRADE_V1` attestation generated from a verified live upgrade. The initial-release path must never be used again once any Release has been published.

## Scope and safeguards

Changing this workflow **does not publish a release**. The workflow remains `workflow_dispatch`-only and must be manually started. Never fabricate a previous-version attestation, skip license checks, or run installation/upgrade against an active DeskMCP installation without a verified safe testing plan.
