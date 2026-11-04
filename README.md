# PCSX2 SaveState Converter

Newer PCSX2 versions refuse to load save states made by older ones. This tool converts your old
`.p2s` files to the newer format so you can carry on where you left off.

## What it can convert

| Made with PCSX2 | Can be converted for |
|---|---|
| 2.0.0 to 2.0.3 | 2.2.0, 2.4.0, 2.6.x, 2.8.x |
| 2.2.0 | 2.4.0, 2.6.x, 2.8.x |
| 2.4.0 | 2.6.x, 2.8.x |
| 2.6.0 to 2.6.3 | 2.8.x |

Each of these was tested by loading converted save states in the official PCSX2 release.
Converting back to an older version isn't supported, and neither is PCSX2 1.6 or older.

### Why not back to an older version?

Think of it like a save file from a newer game version. A newer PCSX2 saves a little more
information than an older PCSX2 knows what to do with.

Going forward is easy: the newer PCSX2 understands the older save and fills in anything
missing. Going backward is not: the older PCSX2 has no idea what the extra information means.
If we threw it away, important details (especially sound) would be lost, and the game might
not continue exactly where you left off.

So the tool only offers conversions that are known to work properly.

## How to use it

1. Grab `PCSX2-SaveState-Converter-<version>-win-x64.exe` from the releases page and run it.
   It's a single file with nothing to install (Windows 10 or 11, 64-bit).
2. It opens `Documents\PCSX2\sstates`. If your save states live somewhere else, pick that folder.
3. Under **Convert for**, choose the PCSX2 version you're going to play on.
4. Everything that can be converted is already ticked. Untick anything you want to skip (the
   search box helps with long lists), then click **Convert**.

By default you get converted copies in a separate folder and your originals stay untouched. Copy
the converted files into the `sstates` folder of the newer PCSX2.

If all your PCSX2 versions share one `sstates` folder, pick **Replace the originals** instead.
Every original gets backed up first, and you can put them back from **Backups**.

A few things to keep in mind:

- Use the same game disc (same region and version) the save state was made with.
- Close PCSX2 before replacing files.
- Save states are snapshots, not real saves. Saving to the memory card in game is still the
  safest way to keep your progress.

## Command line

The zip release also comes with a command-line version in `cli\` (needs the .NET 10 Desktop
Runtime):

```
saveconvert scan D:\PCSX2\sstates
saveconvert convert old.p2s --target 2.8.2 --output new.p2s
saveconvert convert-dir D:\PCSX2\sstates --target 2.8.2 --in-place
saveconvert backups
saveconvert restore <backup-set-folder>
```

Run `saveconvert` on its own to see every option.

## Building

- App and tests: install the .NET 10 SDK, then run `dotnet build SaveStateConverter.slnx` and
  `dotnet test SaveStateConverter.slnx`.
- The converter part (`pcsx2-stateconvert.exe`) is built from PCSX2 2.8.2 plus the patches in
  `experimental/`, see [experimental/BUILD.md](experimental/BUILD.md). Drop the exe and its two
  DLLs into a `native` folder at the repo root and local builds will pick it up.
- Release files: `tools/Package-Release.ps1 -NativeDir <folder with pcsx2-stateconvert.exe, zstd.dll and z.dll>`.

## License

GPL-3.0-or-later. The converter is built from PCSX2's own code (GPL-3.0-or-later, PCSX2 Dev
Team). This project isn't affiliated with or endorsed by the PCSX2 team. Third-party licenses are
listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
