# Dependency security remediation (2026-10-14)

Work branch: `feature/dependency-security-20261013`, based on three-channel / multi-root Workspace changes.

## Package changes
- Upgraded `@modelcontextprotocol/client` to 2.3.1.
- Upgraded `@modelcontextprotocol/server` to 2.3.1.
- Upgraded `@modelcontextprotocol/node` to 2.1.1.
- Upgraded `@wonderwhy-er/desktop-commander` to 0.3.0.
- Kept upstream's existing `uuid` 11.1.1 override.
- Set explicit nested-dependency overrides:
  - `sharp` 0.35.5, addressing its listed high-severity advisory;
  - `chokidar` 4.0.3, removing the vulnerable legacy `braces` chain;
  - `argparse` 2.0.1, removing vulnerable `sprintf-js` inherited from `js-yaml@3`.

These nested overrides cross upstream dependency version ranges. Preserve and run the compatibility smoke test as well as the full DeskMCP tests when updating. Do not auto-force-update incompatible dependencies.

## Results observed
- Original lockfile: `npm audit --omit=dev` reported 16 vulnerabilities (1 critical, 9 high, 6 moderate).
- After direct upgrades: 11 vulnerabilities (0 critical).
- After audit fix: 9 vulnerabilities.
- After targeted overrides: `npm audit` and `npm audit --omit=dev` both reported **0 known vulnerabilities** (checked 2026-10-14).
- Reproducible `npm ci --ignore-scripts --audit=true` reported 0 known vulnerabilities.
- `npm test`: 213 passed / 0 failed after updating Node.js dependencies, .NET 10 SDK available.
- `node scripts/security-dependency-smoke.cjs`: passed; checked front-matter YAML parse, Chokidar watch API, Sharp module, md-to-pdf module load.
- The Windows WPF development build and three self-tests had previously passed on the multi-channel branch.

## Security boundaries / limitations
- `npm audit` is an advisory scan; it is not proof that all vulnerabilities or design flaws are absent.
- DeskMCP multi-channel accounts **share** a local Gateway and allowed Workspace roots; files are not isolated per OpenAI account.
- Before installing a test build, validate installer smoke and per-channel live start/stop/restart, migration from the BAT-managed second Tunnel, and multi-root security behavior.
- Do **not** deploy to the working Changhua laptop in the middle of tests.
