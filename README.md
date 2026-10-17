# WRFrontiersDB-CUE4Parse
> C#, Unreal Engine 5, Parse, Export JSON

Exports the assets of **War Robots: Frontiers** to JSON (and PNG for UI icons) using
[CUE4Parse](https://github.com/FabianFG/CUE4Parse). It is the export step of the WRF
database pipeline. It is specific to that game; the generic multi-game tool lives in
[CUE4P-BatchExport](https://github.com/Surxe/CUE4P-BatchExport).

## What it exports

- `.uasset` packages under `WRFrontiers/Content`, as JSON. Materials, animations, audio and
  other prefixes listed in `src/WrfConstants.cs` are skipped.
- Mesh geometry for static and skeletal meshes (`.uemodel`).
- UI icon textures as PNG, from the directories in `src/WrfConstants.cs`.
- `.locres` localization files, as JSON.

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

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone --recurse-submodules https://github.com/Surxe/WRFrontiersDB-CUE4Parse.git
cd WRFrontiersDB-CUE4Parse
dotnet build src/WRFrontiersDB-CUE4Parse.csproj --configuration Release
```

The CUE4Parse submodule is pinned in `.gitmodules`. Run `git submodule update --init` if it is
empty.

## Releases

Pushing a `v*.*.*` tag builds Windows and Linux binaries and attaches them to a GitHub
release. See `RELEASES.md`.
