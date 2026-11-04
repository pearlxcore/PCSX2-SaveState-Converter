# Building the PCSX2 v2.8.2 fork and the converter

Two patches apply to PCSX2 tag `v2.8.2` (commit `fd9d310ccbb6b8b62c976da8886a3c8fd3a10ff3`):

| Patch | Purpose |
|---|---|
| `pcsx2-v2.8.2-ffmpeg.patch` | Builds `GSCapture.cpp` against the Windows dependency bundle used locally (libavcodec 63, which no longer exposes `AVCodec::pix_fmts`/`sample_fmts`). Unrelated to save states. |
| `pcsx2-v2.8.2-legacy-reader.patch` | The save-state converter (`pcsx2/SaveStateLegacy.{h,cpp}`), its integration into `SaveState_UnzipFromDisk`, the `-diagnose-legacy-state`/`-convert-legacy-state` command-line flags, and the standalone `pcsx2-stateconvert` tool project. |

## Checkout and dependencies

1. Check out `v2.8.2` **outside `%TEMP%`**. MSBuild's file tracker ignores the temp directory, so a tree under `%TEMP%` rebuilds every project on every build (warning MSB8029). Locally the checkout lives in this project's git-ignored `work\` folder (`work\pcsx2-v2.8.2-compat`).
2. Extract the PCSX2 Windows dependency archive so `deps\` sits next to `PCSX2_qt.sln`. The archive used here has SHA-256 `A3133C7841E8FBDAB6F4D1D5AD8EFEC31FB1ECDA95FFED39F4D1F36BB7EB0007`.
3. `git apply pcsx2-v2.8.2-ffmpeg.patch` then `git apply pcsx2-v2.8.2-legacy-reader.patch`.

## Emulator (development configuration)

```powershell
MSBuild PCSX2_qt.sln /t:pcsx2-qt /m:4 /p:Configuration=Devel /p:Platform=x64
```

`Devel` is optimized but uses incremental linking without LTCG: a full build takes about 3 minutes on 12 cores and a rebuild after editing the converter about 20 seconds (`Release` relinks with LTCG for several minutes). Output: `bin\pcsx2-qtx64-dev.exe`.

Run against an isolated data directory; `-datapath X` uses `X\PCSX2` as the data root:

```powershell
pcsx2-qtx64-dev.exe -datapath D:\work\data -diagnose-legacy-state legacy.p2s        # convert in memory, print report
pcsx2-qtx64-dev.exe -datapath D:\work\data -convert-legacy-state legacy.p2s out.p2s # write a native 0x9A59 archive
pcsx2-qtx64-dev.exe -datapath D:\work\data -statefile legacy.p2s -- game.iso         # load a legacy state directly
```

Paths must use backslashes: PCSX2 opens files through `\\?\` long paths, which do not accept `/`.

## Standalone converter

`pcsx2-stateconvert` compiles the same `pcsx2/SaveStateLegacy.cpp` with the core's include paths, defines and forced `PrecompiledHeader.h`, and links only `common`, `fmt` and `libzip` (plus `zstd`/`zlib` from `deps`). It is not part of `PCSX2_qt.sln`; build it directly:

```powershell
MSBuild pcsx2-stateconvert\pcsx2-stateconvert.vcxproj /m:4 /p:Configuration=Release /p:Platform=x64 "/p:SolutionDir=C:/path/to/pcsx2/"
```

Quote the `SolutionDir` argument when the path contains spaces, as it does under this project's folder; the build works from such a path.

Output: `bin\pcsx2-stateconvert.exe` (`-dev` suffix for `Devel`), which needs `zstd.dll` and `z.dll` from `deps\bin`. Usage:

```text
pcsx2-stateconvert [--json] inspect <state.p2s>
pcsx2-stateconvert [--json] convert <legacy.p2s> <output.p2s>
pcsx2-stateconvert [--json] formats
```

Exit codes: 0 success, 1 not convertible or invalid input, 2 usage, 3 output not written.
The tool's output has been checked to be byte-identical, entry by entry, to the archive written by the emulator's `-convert-legacy-state`.
