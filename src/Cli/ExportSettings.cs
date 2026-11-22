using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BatchExport.Cli
{
    /// <summary>
    /// The command-line options. Everything else is a constant in WrfConstants.
    /// </summary>
    public sealed class ExportSettings : CommandSettings
    {
        [CommandOption("--pak-files-directory <PATH>")]
        [Description("Directory containing the game's .pak files (required)")]
        public string? PakFilesDirectory { get; set; }

        [CommandOption("--export-output-path <PATH>")]
        [Description("Directory the export is written to (required)")]
        public string? ExportOutputPath { get; set; }

        [CommandOption("--mapping-file-path <PATH>")]
        [Description("Path to the .usmap mappings file (required)")]
        public string? MappingFilePath { get; set; }

        [CommandOption("--quiet")]
        [Description("Turn off progress logging (on by default)")]
        [DefaultValue(false)]
        public bool Quiet { get; set; }

        [CommandOption("--phases <LIST>")]
        [Description("Comma-separated phases to run, in their fixed order: assets, meshes, maps, config (default: all)")]
        [DefaultValue("assets,meshes,maps,config")]
        public string Phases { get; set; } = "assets,meshes,maps,config";

        /// <summary>The phases to run. A partial run only rewrites what those phases write.</summary>
        public HashSet<string> PhaseSet => Phases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(phase => phase.ToLowerInvariant()).ToHashSet();

        /// <summary>Per-asset progress and warnings are logged unless --quiet is given.</summary>
        public bool IsLoggingEnabled => !Quiet;

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(PakFilesDirectory))
                return ValidationResult.Error("--pak-files-directory is required");
            if (string.IsNullOrWhiteSpace(ExportOutputPath))
                return ValidationResult.Error("--export-output-path is required");
            if (string.IsNullOrWhiteSpace(MappingFilePath))
                return ValidationResult.Error("--mapping-file-path is required");
            if (!Directory.Exists(PakFilesDirectory))
                return ValidationResult.Error($"Pak files directory not found: {PakFilesDirectory}");
            if (!File.Exists(MappingFilePath))
                return ValidationResult.Error($"Mappings file not found: {MappingFilePath}");
            var unknown = PhaseSet.Except(["assets", "meshes", "maps", "config"]).ToList();
            if (unknown.Count > 0 || PhaseSet.Count == 0)
                return ValidationResult.Error($"--phases: unknown or empty phase list '{Phases}'");

            return ValidationResult.Success();
        }
    }
}
