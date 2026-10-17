using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Versions;
using BatchExport.Enums;

namespace BatchExport
{
    /// <summary>
    /// Everything specific to War Robots: Frontiers. These are constants, not options:
    /// changing them means changing this file (and rebuilding), not passing flags.
    /// </summary>
    public static class WrfConstants
    {
        public const EGame UnrealEngineVersion = EGame.GAME_UE5_4;
        public const ETexturePlatform TexturePlatform = ETexturePlatform.DesktopMobile;

        public const ETextureFormat TextureFormat = ETextureFormat.Png;

        /// <summary>Linux build of the Detex texture library, shipped next to the executable.</summary>
        public const string LinuxDetexLibrary = "libdetex.so";

        /// <summary>Assets exported to JSON or mesh files. Maps are handled separately.</summary>
        public static readonly string[] AssetFileExtensions = [".uasset", ".locres"];

        /// <summary>Only paths under this root are exported.</summary>
        public const string NeededExportsRoot = "WRFrontiers/Content";

        /// <summary>Asset names with these prefixes are skipped (materials, animations, audio, ...).</summary>
        public static readonly string[] SkipAssetNamePrefixes = ["M_", "MI_", "MF_", "MLI_", "ABP_", "AS_", "CR_", "AM_", "AKE_"];

        /// <summary>Only textures under these asset-path prefixes (UI icons, robot customization) are decoded to PNG.</summary>
        public static readonly string[] TextureExportDirectories =
        [
            "WRFrontiers/Content/Sparrow/UI",
            "WRFrontiers/Content/Sparrow/Art/TechnicalMaterials/RobotCustomization"
        ];

        /// <summary>
        /// Textures whose PNG is skipped because it is too large to decode (about 3 GB each).
        /// Their JSON is still exported. Ethan confirmed these textures are not used.
        /// </summary>
        public static readonly string[] TextureExportExcludes = ["T_Stickers", "T_Decals"];

        /// <summary>Export mesh geometry for meshes (SM_, SK_, SKEL_ prefixes), not just textures.</summary>
        public const bool ExportMeshes = true;

        /// <summary>Assets exported in the mesh phase, by name prefix.</summary>
        public static readonly string[] MeshAssetPrefixes = ["SM_", "SK_", "SKEL_"];

        // Worker counts are computed per phase from the GC heap budget:
        //   workers = clamp((budget - ProviderBytes) / <phase>WorkerBytes, 1, processor count)
        // The estimates are peaks measured on the home server with one worker, rounded up.
        // The mesh figure is high on purpose: mesh peaks ran out of memory at one worker per core.
        public const long ProviderBytes = 200L * 1024 * 1024;
        public const long AssetWorkerBytes = 600L * 1024 * 1024;
        public const long MeshWorkerBytes = 2048L * 1024 * 1024;

        // --- Maps ---

        /// <summary>Maps and the .ini config files are written here, under the export output path.</summary>
        public const string MapExportDirectory = "map-export";

        /// <summary>Released maps live at MapsRoot/&lt;Key&gt;/&lt;Key&gt; (the persistent level is named after its folder).</summary>
        public const string MapsRoot = "WRFrontiers/Content/Sparrow/Maps";

        /// <summary>The matchmaking data: its MapsLocalization names every map a queue can pick.</summary>
        public const string QueueCommonDataPackage = "WRFrontiers/Content/Sparrow/UI/Meta/QueueModes/DataAssets/DA_QueueCommonData";
        public const string QueueCommonDataType = "SQueueModesCommonDataAsset";

        /// <summary>The map name string table; it also names maps no queue picks (the tutorial).</summary>
        public const string MapNamesPackage = "WRFrontiers/Content/Sparrow/Localization/ST_MapNames";
        public const string MapNameKeyPrefix = "Map_";

        /// <summary>Played, but named nowhere.</summary>
        public static readonly string[] UnnamedMaps = ["FiringRange"];

        /// <summary>
        /// Meshes maps reference that are not cooked into the game, so not loading them is not a failure.
        /// They are still listed in the scene's warnings.
        /// The Houdini Engine placeholder is an editor-only reference left on some actors.
        /// </summary>
        public static readonly string[] ExpectedMissingMeshes =
        [
            "/HoudiniEngine/default_reference_static_mesh.default_reference_static_mesh"
        ];
    }
}
