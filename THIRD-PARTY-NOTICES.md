# Third-party notices

PCSX2 SaveState Converter is distributed under the GNU General Public License v3.0 or later
(`LICENSE`). It includes or links the following components.

| Component | Used by | License | Text |
|---|---|---|---|
| PCSX2 (© 2002-2026 PCSX2 Dev Team): `pcsx2/SaveStateLegacy.cpp` is a modification of PCSX2; `pcsx2-stateconvert.exe` compiles it against PCSX2 headers and links PCSX2's `common` library | converter, experimental loader patch | GPL-3.0-or-later | `LICENSE` |
| {fmt} | converter | MIT-style ({fmt} license) | `licenses/fmt-LICENSE.txt` |
| libzip | converter | BSD-3-Clause | `licenses/libzip-LICENSE.txt` |
| Zstandard (`zstd.dll`) | converter | BSD-3-Clause | `licenses/zstd-LICENSE.txt` |
| zlib (`z.dll`) | converter | zlib License | `licenses/zlib-LICENSE.txt` |
| SharpCompress | .NET applications | MIT | `licenses/SharpCompress-LICENSE.txt` |
| Game titles (`catalog/game-titles.tsv`), extracted from PCSX2's `bin/resources/GameIndex.yaml` at tag `v2.8.2` | GUI (game names in the list) | GPL-3.0-or-later | `LICENSE` |

Corresponding source for the converter: PCSX2 tag `v2.8.2`
(commit `fd9d310ccbb6b8b62c976da8886a3c8fd3a10ff3`, https://github.com/PCSX2/pcsx2) plus the
patches in `experimental/`; build instructions are in `experimental/BUILD.md`.

PCSX2 is a trademark of its developers. This project is not affiliated with or endorsed by the
PCSX2 team. No BIOS, game data or PCSX2 binaries are included (the title list holds only disc
serials and game names).
