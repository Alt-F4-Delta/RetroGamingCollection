# RetroGaming Collection

A Windows x64 launcher inspired by the blue/purple ZSNES browser. Browse your own SNES and Genesis files, search titles and platforms, filter by filename language/region, and launch games in a configured RetroArch installation.

## Install and play

Download the Windows installer from [Releases](https://github.com/Alt-F4-Delta/RetroGamingCollection/releases). The installer includes the .NET runtime and installs for the current Windows user without requiring administrator privileges. Windows may show a publisher warning because project installers are not code-signed.

Games, BIOS files and emulator binaries are not included. Configure an existing trusted RetroArch installation and the Snes9x/Genesis Plus GX cores in `Configuration/settings.json` using **SETTINGS** to find that file. Use **IMPORT FOLDER** to register legally obtained cartridge files. Original games stay in their source folders.

Select a game, then click **LAUNCH GAME**, press Enter, or double-click. Close the emulator window to return. Configured keyboard controls: arrows move; Enter starts; Right Shift selects; SNES Z/X/A/S = B/A/Y/X and Q/W = L/R; Genesis A/Z/X = A/B/C and Q/S/W = X/Y/Z. F1 opens the emulator menu. Controller mappings depend on the device and emulator configuration.

The default view shows USA releases without explicit language tags. Shared regions such as `(USA, Europe)` are included. Choose a language or **All languages**, or uncheck **USA only** to broaden the view. Untagged does not guarantee English. Search and filters work together; filter preferences are saved.

## Updates and changelogs

Click **CHECK FOR UPDATES**. The popup shows changes since your installed version before you choose **DOWNLOAD AND INSTALL**. You can defer or cancel. The app verifies the downloaded installer's size and SHA-256 checksum, checks the GitHub asset digest when supplied, and opens Setup only after verification. Setup remains interactive and can be cancelled. Close any running game before updating.

Releases use hundredths: **1.00 → 1.01 → 1.02**, eventually **1.99 → 2.00**. Each push to `main` starts a build/release workflow; its version counter is `99 + GITHUB_RUN_NUMBER`. Re-running the same workflow uses the same version and leaves an already-published release unchanged. Manual workflow dispatch creates the next release. Failed or superseded queued builds can leave gaps; only complete published stable releases are offered to users.

Release notes list commit subjects since the previous published release. Write clear commit messages; use `CHANGELOG.md` for the initial release notes. New releases are kept as drafts until the installer, portable ZIP and checksums have all uploaded. The app ignores prereleases and releases without the required assets. If multiple stable updates were skipped, their notes appear together.

The public release feed requires no GitHub token in the app. It is fixed to `Alt-F4-Delta/RetroGamingCollection`. Checksums detect damaged downloads; they do not replace an Authenticode publisher signature. Protect repository write access and release credentials.

## Data and portable migration

Installed data lives in `%LOCALAPPDATA%\RetroGamingCollection`, separate from `%LOCALAPPDATA%\Programs\RetroGamingCollection`. Upgrading program files does not replace the library, settings or saves. Uninstalling leaves user data in place.

The portable build stores data beside its executable. Run the updated portable app once before installing to register its location on this PC. On the installed app's first launch, it can import those local library registrations, filter settings and emulator paths without copying ROMs. Existing portable save paths are retained in place; keep that folder and its emulator dependencies. Existing installed settings are never overwritten by this migration.

**RESCAN** rechecks configured cartridge folders. Cached registrations remain visible when the drive is disconnected. A recorded Windows volume GUID can remap the original library's drive letter. Unknown formats and ambiguous multi-ROM ZIP files are not launched automatically. Platform recognition is based on cartridge headers, not a complete authoritative title database; filenames may be incorrect.

For a fresh setup, settings can reference an existing emulator, for example:

```json
{
  "RetroArchPath": "C:\\Emulators\\RetroArch\\retroarch.exe",
  "SnesCorePath": "C:\\Emulators\\RetroArch\\cores\\snes9x_libretro.dll",
  "GenesisCorePath": "C:\\Emulators\\RetroArch\\cores\\genesis_plus_gx_libretro.dll",
  "LibraryPaths": ["E:\\MyGames"],
  "LanguageFilter": "Untagged",
  "UsaOnly": true
}
```

## Build and verify

Requires Windows, .NET 10 SDK and PowerShell 7.

```powershell
dotnet build -c Release
dotnet run --project tools/UpdateValidation/UpdateValidation.csproj -c Release
./tools/build-release.ps1
```

The release script obtains the unpacked Inno Setup 6.7.3 compiler package and verifies its publisher signature; no compiler installation is performed. Inno Setup licensing applies to commercial compiler use. The script builds a self-contained app, installer, portable ZIP and `SHA256SUMS.txt` under `artifacts/release-1.00`. Pass `-Counter 101` to build 1.01 locally. Local default counter is in `version.txt`; release tags and workflow run numbers are the authoritative published versions. No bot commits are pushed, avoiding recursive version-bump builds.

The synthetic validation suite checks version ordering/rollover, release selection, cumulative notes, checksum and size enforcement, cancellation, untrusted URLs, network failure, filters and migration preservation. Optional `tools/Validation` integration checks require a local library; they are not run by CI. Set `RETROGAMING_DATA_DIRECTORY` only when deliberately isolating test data.

After publishing a release, `dotnet run --project tools/UpdateValidation/UpdateValidation.csproj -c Release -- --live` verifies the real public GitHub feed, changelog, installer download and checksums without running the downloaded installer. `tools/test-installer.ps1` tests two installer versions in an isolated `artifacts/install-smoke` directory, checks preservation, and uninstalls that test copy. It refuses to replace an existing installation elsewhere. CI runs that upgrade check using the previous published installer; local runs also verify the installed app opens with the expected version.

Local ROMs, emulators, BIOS files, saves, personal settings, collection reports, recovery helpers and build outputs are excluded from Git and release packages. Package contents are limited to the compiled application/runtime plus this README and changelog.
