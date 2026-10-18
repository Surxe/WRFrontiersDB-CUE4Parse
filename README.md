# WRFrontiersDB-CUE4Parse
> C#, Unreal Engine 5, Parse, Export JSON

Exports the assets of **War Robots: Frontiers** to JSON (and PNG for UI icons) using
[CUE4Parse](https://github.com/FabianFG/CUE4Parse). It is the export step of the WRF
database pipeline. It is specific to that game; the generic multi-game tool lives in
[CUE4P-BatchExport](https://github.com/Surxe/CUE4P-BatchExport).

## What it exports

One run exports everything, in phases:

1. **Assets**: `.uasset` packages under `WRFrontiers/Content`, as JSON, plus UI icon textures as
   PNG and `.locres` localization files as JSON. Materials, animations, audio and the other
   prefixes in `src/WrfConstants.cs` are skipped.
2. **Meshes**: static and skeletal meshes (`SM_`, `SK_`, `SKEL_`), as JSON and `.uemodel` geometry.
3. **Maps**: every released map as a scene, into `<output>/map-export/maps/`. The map list comes
   from the game's own data (see `src/MapList.cs`). See `MAPS.md` for the format.
4. **Config**: the game's `.ini` files, into `<output>/map-export/config/`. The scenes refer to
   the collision profiles they define.

## Usage

```bash
WRFrontiersDB-CUE4Parse \
  --pak-files-directory "/path/to/paks" \
  --export-output-path "/path/to/export" \
  --mapping-file-path "/path/to/mappings.usmap"

# All options
WRFrontiersDB-CUE4Parse --help
```

Options are defined once, in `src/Cli/ExportSettings.cs`. The help text is generated from that
class. Everything else that is specific to War Robots: Frontiers (the Unreal Engine version,
texture settings, which paths to skip, and which textures to export) is a constant in
`src/WrfConstants.cs`. Changing one of those means editing the file and rebuilding.

Progress is logged to the console by default. Pass `--quiet` to turn it off.

### Memory

Worker counts are computed per phase from the memory budget, so there is no worker option.
The budget is the .NET GC heap limit: 75% of the process's memory cap when one is set (for
example `systemd-run --scope -p MemoryMax=1536M`), otherwise physical RAM. The per-worker
estimates are constants in `src/WrfConstants.cs`. The startup log prints the budget and the
worker counts.

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Everything exported. |
| 1 | The export finished, but something failed. The summary lists the failures per phase. |
| 3 | Out of memory. The run stopped at once; raise the memory cap. |

Nothing is skipped silently. A failure that is expected (for example, an editor-only mesh that
maps reference but the game does not ship) is listed by name in `src/WrfConstants.cs`.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone --recurse-submodules https://github.com/Surxe/WRFrontiersDB-CUE4Parse.git
cd WRFrontiersDB-CUE4Parse
dotnet build src/WRFrontiersDB-CUE4Parse.csproj --configuration Release
```

The CUE4Parse submodule is pinned in `.gitmodules`. Run `git submodule update --init` if it is
empty.

### Native libraries

On Linux the export loads three native libraries from the directory of the executable: Oodle
(`liboodle-data-shared.so`), Detex (`libdetex.so`) and SkiaSharp (`libSkiaSharp.so`). The release
zip ships them next to the executable. For a local build, fetch them into the output directory:

```bash
scripts/fetch-linux-natives.sh bin/Release/net10.0
```

Detex is built from source at a pinned commit (`hglm/detex`), because CUE4Parse ships only a
Windows build. Oodle and SkiaSharp are fetched from their release packages at pinned versions.

On Windows only Oodle (`oodle-data-shared.dll`) is shipped next to the executable, fetched from
the same pinned OodleUE release by `scripts/fetch-windows-natives.sh` (it runs in Git Bash).
Detex is embedded in CUE4Parse and SkiaSharp is bundled by the single-file publish.

The export stops with an error if Oodle (or, on Linux, any of the libraries) is missing.

> **Windows is not fully tested.** The export is developed and run on Linux. The Windows build
> compiles in CI, but bundling Oodle has not been run on a Windows machine.

## Releases

Pushing a `v*.*.*` tag builds Windows and Linux binaries and attaches them to a GitHub
release. Both zips include the native libraries above. See `RELEASES.md`.
