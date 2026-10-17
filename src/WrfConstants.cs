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

        /// <summary>Export worker count. Fixed for now; a memory budget will replace it.</summary>
        public const int WorkerCount = 2;
    }
}
