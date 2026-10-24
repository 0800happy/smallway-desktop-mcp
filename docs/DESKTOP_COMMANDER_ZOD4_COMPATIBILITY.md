# Desktop Commander 0.2.47 — Zod 4 compatibility (QA)

## Why this exists

Desktop Commander 0.2.47 declares `zod ^3.24.1`. The npm advisory `GHSA-mrhx-c7xc-m3w5` affects Zod releases earlier than 4.6.6. Updating the top-level Zod alone does **not** remediate its nested Zod 3 copy. A blanket npm override to Zod 4 makes the old `zod-to-json-schema` converter return unusable input schemas for MCP tools, even when all generic Gateway tests pass.

## Reviewed, fail-closed patch

The dependency remains pinned to MIT-licensed `@wonderwhy-er/desktop-commander@0.2.47`. The root `package.json` pins `overrides.zod=4.6.7`. The program `scripts/apply-desktop-commander-zod4.mjs` applies one compatibility change to the **installed copy only**:

- Patch **only** `node_modules/@wonderwhy-er/desktop-commander/dist/server.js`.
- Replace the upstream `zod-to-json-schema` import with Zod 4's `z.toJSONSchema(..., { io: "input", unrepresentable: "any" })` and omit the top-level draft marker for the MCP tool input contract.
- Verify package name/version, runtime Zod 4.6.7, **original SHA-256** `3bc0962a60ff74b5c8a95e1627cb1e8fcb87e5a1bee19426c9e73f5107f84428`, and **patched SHA-256** `68512aaff39ce63b85852516894e8c4362f8aef6cd16566c0169fabc4bb8776d`. If any hash or API differs, reject the build; never patch an unknown upstream version.
- Execute the upstream MCP `tools/list` handler and verify all **26 listed tools** have valid JSON-object input schemas, including required path and command argument types. The underlying Zod 4 converter also successfully converts all 27 registered schema definitions, including five legitimate zero-argument schemas.

The patch is **reproducible and auditable**. It runs as part of the repository build and in isolated production dependency installation for Windows x64/ARM64 and macOS ARM64 release stages. `npm ci --ignore-scripts` alone does not silently patch dependencies: the explicit build/staging script must run the guarded patch.

The MIT-licensed upstream source remains credited, and the generated third-party notices disclose the modification. No upstream Desktop Commander version 0.3.x or telemetry-contract component is introduced.

## Local QA evidence (2026-10-24)

- Clean npm install: **631 installed / 632 audited**, with zero reported known vulnerabilities at the time checked.
- Full Gateway tests: **220 pass, 0 fail**.
- Upstream input schema conversion: **27/27**; actual MCP tools list: **26/26**.
- Windows x64 isolated production npm install: **624 installed / 625 audited, 0 vulnerabilities**.
- Both a fresh patch application and an idempotent second execution pass against a separately staged runtime.
- `scripts/generate-third-party-notices.mjs` unmodified strict validation logic after refreshing both canonical inputs: **578 production Node packages, 7 special licenses, zero UNKNOWN licenses, no missing metadata, no duplicate package identities**.

## Not yet complete

This is a **QA candidate**, not a signed, notarized, distributed, or installed release. Re-run Windows x64 and ARM64 installer builds/smoke and macOS native stage on hosted CI; verify release provenance, source clean checks, SHA-256 manifests, unsigned installation warnings, update rollback, and actual clean-install behavior before any GitHub Release.

Do not apply this patch directly to a live user's installed Desktop Commander. Do not disable npm vulnerability audit or modify the audited Zod constraint as a workaround. An upstream version with native Zod 4 support may eventually replace this compatibility patch, **but only after repeating the same schema and cross-platform tests**.
