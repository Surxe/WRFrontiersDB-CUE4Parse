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

        [CommandOption("--verbose")]
        [Description("Log every asset as it is processed")]
        [DefaultValue(false)]
        public bool Verbose { get; set; }

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

            return ValidationResult.Success();
        }
    }
}
