# Releases

GitHub Actions builds and releases the Windows and Linux binaries.

## Package Contents

Each release package is a `.zip` containing:
- **Self-contained executable**: no .NET runtime installation required
- **Native libraries**, next to the executable:
  - Linux: `liboodle-data-shared.so`, `libdetex.so`, `libSkiaSharp.so`, fetched and built by `scripts/fetch-linux-natives.sh` in CI
  - Windows: `oodle-data-shared.dll`, fetched by `scripts/fetch-windows-natives.sh` in CI (Detex is embedded in the executable, and SkiaSharp is bundled by the single-file publish). The Windows build is not fully tested
- **Documentation**: `README.md`

Asset names: `WRFrontiersDB-CUE4Parse-windows-x64.zip` and `WRFrontiersDB-CUE4Parse-linux-x64.zip`.

## Creating a New Release

1. **Create and push a version tag:**
   ```bash
   git tag v2.0.0
   git push origin v2.0.0
   ```

2. **GitHub Actions will automatically** build both platforms, publish single-file executables,
   and create a GitHub release with the packages attached.

Releases can also be triggered by hand: in the **Actions** tab, choose **Build and Release**,
then **Run workflow**.

## Version Naming

Follow semantic versioning for tags:
- **Major**: `v2.0.0`: breaking changes (for example, a changed command-line interface)
- **Minor**: `v2.1.0`: new features, backward compatible
- **Patch**: `v2.0.1`: bug fixes, backward compatible

Earlier releases (`v1.x`) are on the [CUE4P-BatchExport](https://github.com/Surxe/CUE4P-BatchExport/releases)
repository. They use the generic command-line interface and presets.

## Quick Start After Download

```bash
# Windows
.\WRFrontiersDB-CUE4Parse.exe --pak-files-directory "C:\Game\Paks" --export-output-path "C:\Export" --mapping-file-path "C:\mappings.usmap"

# Linux
./WRFrontiersDB-CUE4Parse --pak-files-directory "/path/to/paks" --export-output-path "/path/to/export" --mapping-file-path "/path/to/mappings.usmap"
```

For all options: `WRFrontiersDB-CUE4Parse --help`. See the [README](README.md).
