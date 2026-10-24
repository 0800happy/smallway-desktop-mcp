# Third-Party Notices

This notice is generated from the actual Windows x64 release stage for Smallway Desktop MCP 0.9.18. Smallway Desktop MCP itself is licensed under Apache License 2.0; see the repository root `LICENSE`. Third-party components retain the licenses documented below.

## Bundled runtimes and major components

- **Node.js 24.19.0** — distributed with its upstream `node/LICENSE`, which includes Node.js and bundled third-party notices.
- **.NET 10 Windows x64 self-contained runtime** — the release carries `licenses/dotnet/LICENSE.txt` and `licenses/dotnet/ThirdPartyNotices.txt` copied from the exact SDK used to publish the desktop application.
- **Microsoft WinApp CLI v0.5.0** — MIT; Smallway Desktop MCP bundles the pinned standalone `winapp.exe` + `libSkiaSharp.dll` computer-use backend and preserves its MIT notice at `licenses/winappcli/LICENSE.txt`.
- **VirtualDesktopAccessor** — MIT; Smallway Desktop MCP builds the pinned source commit recorded in `virtual-desktop-accessor/SOURCE_COMMIT.txt`, preserves the upstream MIT notice at `virtual-desktop-accessor/LICENSE.txt`, and records the shipped DLL checksum in `virtual-desktop-accessor/SHA256SUMS.txt`.
- **OpenAI tunnel-client v0.0.13** — Apache-2.0; its upstream `LICENSE`, `NOTICE`, third-party licenses text, and SPDX document remain under `tunnel-client/v0.0.13/bin/`.
- **@wonderwhy-er/desktop-commander 0.2.47** — MIT. Its package-local MIT license remains in the bundled production; Smallway applies a SHA-256-pinned compatibility patch to its dist/server.js for Zod 4.6.7 input-schema conversion (source: scripts/apply-desktop-commander-zod4.mjs). This is a modified upstream build, not an unmodified upstream binary.
- **sharp-win32-x64 0.35.5** — package metadata declares Apache-2.0 AND LGPL-3.0-or-later. Its package-local LICENSE and README, including the bundled libvips/native-library license table, remain in the release.

## Manual license resolution

### buffers 0.1.1

The npm package metadata does not declare a license and the published tarball contains no LICENSE file. Debian's reviewed source record for node-buffers 0.1.1-2 identifies the upstream package as MIT and links the upstream commit that added the declaration. A copy of that MIT notice is included as `licenses/buffers-0.1.1-MIT.txt`.

Evidence:
- https://www.npmjs.com/package/buffers
- https://sources.debian.org/copyright/license/node-buffers/0.1.1-2/
- https://github.com/substack/node-buffers/commit/1b745ee35d33eb166e15ef1866073a07c6d7de87

### Pinned ssh2 runtime dependencies

For ssh2@1.17.0, cpu-features@0.0.10, and buildcheck@0.0.7, npm package metadata omits the license field. The bundled package-local LICENSE files each contain the MIT permission grant and warranty disclaimer (copyright Brian White). The release generator verifies those bundled MIT texts for these exact versions before assigning the manual MIT resolution. All three original package-local LICENSE files remain in the shipped payload.

## Production Node package inventory

The complete machine-generated inventory is `licenses/production-node-packages.csv`. It records package name, version, resolved license expression, package-local license files, and its path inside the target release stage.

Package/license counts for this stage:
- MIT: 501
- ISC: 31
- Apache-2.0: 15
- BSD-3-Clause: 7
- MIT (manual resolution): 4
- BSD-2-Clause: 4
- BlueOak-1.0.0: 4
- Unlicense: 2
- (MIT AND Zlib): 2
- 0BSD: 2
- Apache-2.0 AND LGPL-3.0-or-later: 1
- Python-2.0: 1
- MIT/X11: 1
- (MIT OR GPL-3.0-or-later): 1
- (WTFPL OR MIT): 1
- (MIT OR GPL-3.0): 1

For `jszip` and `pizzip`, this distribution elects the MIT option expressly offered by their dual-license files.

License expressions requiring special attention or explicit choice:
- `@img/sharp-win32-x64@0.35.5` — Apache-2.0 AND LGPL-3.0-or-later; files: LICENSE
- `buffers@0.1.1` — MIT (manual resolution)
- `buildcheck@0.0.7` — MIT (manual resolution); files: LICENSE
- `cpu-features@0.0.10` — MIT (manual resolution); files: LICENSE
- `jszip@3.10.1` — (MIT OR GPL-3.0-or-later); files: LICENSE.markdown
- `pizzip@3.2.0` — (MIT OR GPL-3.0); files: LICENSE.markdown
- `ssh2@1.17.0` — MIT (manual resolution); files: LICENSE

## Preservation rule

Do not strip package-local LICENSE, NOTICE, COPYING, COPYRIGHT, README license tables, Node's LICENSE, platform runtime notices, WinApp CLI's MIT notice, VirtualDesktopAccessor's MIT/provenance/checksum files, or tunnel-client notice/SPDX files when optimizing a release payload.
