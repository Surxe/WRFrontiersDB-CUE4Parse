using BatchExport.Cli;
using Spectre.Console.Cli;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures.BC;
using Newtonsoft.Json;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System.Collections.Concurrent;

namespace BatchExport
{
    public static class BatchExportProgram
    {
        //Create all preceding directories of a given file if they don't yet exist
        private static void CreateNeededDirectories(string destinationFilePath)
        {
            // destinationFilePath is a full path already rooted at ExportOutputPath,
            // so create its parent directory directly. (The previous version split
            // the full path and re-prepended the base, doubling it — the parent was
            // never created and locres writes failed with DirectoryNotFoundException.)
            var directory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        //Extract an asset and write it to a JSON file or image file
        private static void ExtractAsset(DefaultFileProvider gameFileProvider, string assetPath, ExportSettings settings)
        {
            try
            {
                // Check if this is a .locres file (localization resource)
                if (assetPath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractLocresFile(gameFileProvider, assetPath, settings);
                    return;
                }

                // Handle regular UE packages (.uasset)
                var exporterOptions = new ExporterOptions
                {
                    TextureFormat = WrfConstants.TextureFormat,
                    Platform = WrfConstants.TexturePlatform,
                    ExportHdrTexturesAsHdr = true,
                    // Geometry is untextured; materials and morph targets are not needed.
                    ExportMaterials = false,
                    ExportMorphTargets = false
                };

                var exporter = new AssetExporter(exporterOptions, settings.ExportOutputPath!, settings.IsLoggingEnabled);
                exporter.ExportAsset(gameFileProvider, assetPath);
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to extract asset {assetPath}: {ex.Message}", settings.IsLoggingEnabled);
                if (settings.IsLoggingEnabled)
                {
                    Utils.LogInfo($"Stack trace: {ex.StackTrace}", settings.IsLoggingEnabled);
                    if (ex.InnerException != null)
                    {
                        Utils.LogInfo($"Inner exception: {ex.InnerException.Message}", settings.IsLoggingEnabled);
                        Utils.LogInfo($"Inner stack trace: {ex.InnerException.StackTrace}", settings.IsLoggingEnabled);
                    }
                }
            }
        }

        private static void ExtractLocresFile(DefaultFileProvider gameFileProvider, string assetPath, ExportSettings settings)
        {
            try
            {
                // Get the game file for the locres
                var gameFile = gameFileProvider.Files[assetPath];
                if (gameFile != null)
                {
                    using var archive = gameFile.CreateReader();

                    // Parse the locres file using CUE4Parse
                    var locres = new FTextLocalizationResource(archive);

                    // Serialize the locres object directly to JSON
                    string serializedJson = JsonConvert.SerializeObject(locres, Formatting.Indented);

                    // Destination path within exports directory (change extension to .json)
                    string destinationFilePath = settings.ExportOutputPath + "/" + assetPath.Replace(".locres", ".json");

                    // Create the directories if they don't exist
                    CreateNeededDirectories(destinationFilePath);

                    // Write the JSON to file
                    File.WriteAllText(destinationFilePath, serializedJson);
                }
                else
                {
                    Console.WriteLine($"Could not find locres file: {assetPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred when parsing locres file {assetPath}: {ex.Message}");
            }
        }

        private static bool ShouldProcessFile(string assetFilePath, List<string> targetExportDirectories)
        {
            // Check file extension
            if (!WrfConstants.AssetFileExtensions.Any(ext => assetFilePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Skip engine files
            if (assetFilePath.StartsWith("engine", StringComparison.OrdinalIgnoreCase))
                return false;

            // Skip explicitly-ignored asset classes (materials, animations, audio, ...).
            string fileName = Path.GetFileNameWithoutExtension(assetFilePath);
            if (WrfConstants.SkipAssetNamePrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Check if file is in any export directory (empty string means export all)
            return targetExportDirectories.Any(dir => string.IsNullOrEmpty(dir) || assetFilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        private static DefaultFileProvider CreateIndependentProvider(ExportSettings settings)
        {
            var provider = new DefaultFileProvider(
                settings.PakFilesDirectory!,
                SearchOption.AllDirectories,
                new VersionContainer(WrfConstants.UnrealEngineVersion, WrfConstants.TexturePlatform),
                StringComparer.Ordinal
            );
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingFilePath!);
            provider.Initialize();
            provider.Mount();
            provider.PostMount();
            provider.ChangeCulture("en");
            return provider;
        }

        /// <summary>
        /// Loads Oodle (decompression) and Detex (BC textures). On Linux they are shipped next to
        /// the executable and loaded by full path, so the export needs no environment setup and
        /// never downloads anything. A missing library is an error, not a silent texture loss.
        /// </summary>
        private static void LoadNativeLibraries(ExportSettings settings)
        {
            if (OperatingSystem.IsLinux())
            {
                string oodlePath = Path.Combine(AppContext.BaseDirectory, OodleHelper.OodleFileName);
                string detexPath = Path.Combine(AppContext.BaseDirectory, WrfConstants.LinuxDetexLibrary);
                RequireNativeLibrary(oodlePath);
                RequireNativeLibrary(detexPath);

                Utils.LogInfo($"Initializing Oodle from {oodlePath}...", settings.IsLoggingEnabled);
                OodleHelper.Initialize(oodlePath);

                Utils.LogInfo($"Initializing Detex from {detexPath}...", settings.IsLoggingEnabled);
                DetexHelper.Initialize(detexPath);
                return;
            }

            Utils.LogInfo("Initializing Oodle...", settings.IsLoggingEnabled);
            OodleHelper.DownloadOodleDll();
            OodleHelper.Initialize(OodleHelper.OodleFileName);

            Utils.LogInfo("Initializing Detex...", settings.IsLoggingEnabled);
            DetexHelper.LoadDll();
            DetexHelper.Initialize(DetexHelper.DLL_NAME);
        }

        private static void RequireNativeLibrary(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Required native library is missing: {path}. The Linux release ships it next to the executable.");
        }

        public static int Main(string[] args)
        {
            return new CommandApp<ExportCommand>().Run(args);
        }

        public static int Export(ExportSettings settings)
        {
            Utils.LogInfo("Output Directory: " + settings.ExportOutputPath, settings.IsLoggingEnabled);

            // Ensure output directory exists
            if (!Directory.Exists(settings.ExportOutputPath))
            {
                Directory.CreateDirectory(settings.ExportOutputPath!);
                Utils.LogInfo("Created output directory.", settings.IsLoggingEnabled);
            }

            // Decrypt .pak to assets
            if (settings.IsLoggingEnabled)
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
            }

            LoadNativeLibraries(settings);

            Utils.LogInfo("Creating version container and file provider...", settings.IsLoggingEnabled);
            Utils.LogInfo($"Game directory: {settings.PakFilesDirectory}", settings.IsLoggingEnabled);
            Utils.LogInfo($"Mappings file: {settings.MappingFilePath}", settings.IsLoggingEnabled);
            Utils.LogInfo("Creating provider...", settings.IsLoggingEnabled);
            Utils.LogInfo($"Using UE version: {WrfConstants.UnrealEngineVersion}, Texture platform: {WrfConstants.TexturePlatform}", settings.IsLoggingEnabled);

            var fileProvider = new DefaultFileProvider(settings.PakFilesDirectory!, SearchOption.AllDirectories, new VersionContainer(WrfConstants.UnrealEngineVersion, WrfConstants.TexturePlatform), StringComparer.Ordinal);

            Utils.LogInfo("Setting mappings...", settings.IsLoggingEnabled);
            fileProvider.MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingFilePath!);

            Utils.LogInfo("Initializing provider...", settings.IsLoggingEnabled);
            fileProvider.Initialize();
            Utils.LogInfo($"Files found after Initialize(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            Utils.LogInfo("Mounting provider...", settings.IsLoggingEnabled);
            fileProvider.Mount();
            Utils.LogInfo($"Files found after Mount(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            Utils.LogInfo("Post-mounting provider...", settings.IsLoggingEnabled);
            fileProvider.PostMount();
            Utils.LogInfo($"Files found after PostMount(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            fileProvider.ChangeCulture("en");
            Utils.LogInfo($"Total files found by provider: {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            List<string> exportDirectoriesToProcess = [WrfConstants.NeededExportsRoot];
            Utils.LogInfo("Narrowed Directories that will be exported:", settings.IsLoggingEnabled);
            foreach (var exportDirectory in exportDirectoriesToProcess)
            {
                Utils.LogInfo("\t" + exportDirectory, settings.IsLoggingEnabled);
            }

            // Export to .json
            Console.WriteLine("Starting file export...");
            Utils.LogInfo("Please wait while the script exports files...", settings.IsLoggingEnabled);

            var allFiles = fileProvider.Files.Select(kvp => kvp.Value.ToString()).ToList();

            int totalFilesProcessed = 0;
            int totalFilesExported = 0;

            // Each worker builds a full independent DefaultFileProvider (mounts every pak +
            // the VFS index), so peak memory scales with the worker count.
            int workerCount = WrfConstants.WorkerCount;
            Utils.LogInfo($"Using {workerCount} export worker(s)", settings.IsLoggingEnabled);

            // Shared concurrent work queue
            var workQueue = new ConcurrentQueue<string>(allFiles);

            // Run N workers pulling from the queue until empty
            Parallel.ForEach(Enumerable.Range(0, workerCount), new ParallelOptions { MaxDegreeOfParallelism = workerCount }, _ =>
                {
                    var localProvider = CreateIndependentProvider(settings);

                    while (workQueue.TryDequeue(out var currentFilePath))
                    {
                        Interlocked.Increment(ref totalFilesProcessed);

                        if (!ShouldProcessFile(currentFilePath, exportDirectoriesToProcess))
                            continue;

                        string assetPathForExport = currentFilePath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase)
                            ? currentFilePath
                            : currentFilePath.Replace(".uasset", "");

                        Utils.LogInfo("Exporting asset: " + assetPathForExport, settings.IsLoggingEnabled);
                        ExtractAsset(localProvider, assetPathForExport, settings);
                        Interlocked.Increment(ref totalFilesExported);
                    }
                });

            Utils.LogInfo($"Processing complete. Files processed: {totalFilesProcessed}, Files exported: {totalFilesExported}", settings.IsLoggingEnabled);
            return 0;
        }
    }
}
