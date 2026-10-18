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
using System.Diagnostics;
using System.Runtime;

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
                Failures.Record(assetPath, ex);
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
                    Failures.Record(assetPath, "locres file not found in the provider");
                    Console.WriteLine($"Could not find locres file: {assetPath}");
                }
            }
            catch (Exception ex)
            {
                Failures.Record(assetPath, ex);
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
        /// Loads Oodle (decompression) and Detex (BC textures). The release ships Oodle next to the
        /// executable on both platforms (and Detex too on Linux), loaded by full path, so the export
        /// needs no environment setup and never downloads anything. A missing library is an error,
        /// not a silent texture loss. On Windows, Detex is extracted from CUE4Parse's embedded copy.
        /// </summary>
        private static void LoadNativeLibraries(ExportSettings settings)
        {
            string oodlePath = Path.Combine(AppContext.BaseDirectory, OodleHelper.OodleFileName);
            RequireNativeLibrary(oodlePath);
            Utils.LogInfo($"Initializing Oodle from {oodlePath}...", settings.IsLoggingEnabled);
            OodleHelper.Initialize(oodlePath);

            if (OperatingSystem.IsLinux())
            {
                string detexPath = Path.Combine(AppContext.BaseDirectory, WrfConstants.LinuxDetexLibrary);
                RequireNativeLibrary(detexPath);
                Utils.LogInfo($"Initializing Detex from {detexPath}...", settings.IsLoggingEnabled);
                DetexHelper.Initialize(detexPath);
                return;
            }

            Utils.LogInfo("Initializing Detex...", settings.IsLoggingEnabled);
            DetexHelper.LoadDll();
            DetexHelper.Initialize(DetexHelper.DLL_NAME);
        }

        private static void RequireNativeLibrary(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Required native library is missing: {path}. The release ships it next to the executable.");
        }

        private static bool IsMeshAsset(string assetPath)
        {
            string fileName = Path.GetFileName(assetPath);
            return WrfConstants.MeshAssetPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Workers for a phase from the memory budget. Worker 0 is the main provider; each extra worker
        /// mounts its own provider and needs the phase's working set on top.
        /// </summary>
        private static int WorkersFor(long budget, long workerBytes)
        {
            long fit = (budget - WrfConstants.ProviderBytes) / workerBytes;
            return (int)Math.Clamp(fit, 1, Environment.ProcessorCount);
        }

        /// <summary>Drops what the last phase left behind, so the next one starts from the baseline.</summary>
        private static void CollectBetweenPhases()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }

        private static (string, int, int, TimeSpan) RunAssetPhase(string phase, List<string> files, int workers, DefaultFileProvider mainProvider, ExportSettings settings)
        {
            Failures.Phase = phase;
            Console.WriteLine($"Phase {phase}: {files.Count} file(s) on {workers} worker(s)...");
            var stopwatch = Stopwatch.StartNew();

            var workQueue = new ConcurrentQueue<string>(files);
            int exported = 0;
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
            {
                // Worker 0 reuses the main provider; the others mount their own.
                DefaultFileProvider? ownProvider = worker == 0 ? null : CreateIndependentProvider(settings);
                var provider = ownProvider ?? mainProvider;
                try
                {
                    while (workQueue.TryDequeue(out var assetPath))
                    {
                        Utils.LogInfo("Exporting asset: " + assetPath, settings.IsLoggingEnabled);
                        ExtractAsset(provider, assetPath, settings);
                        Interlocked.Increment(ref exported);
                    }
                }
                finally
                {
                    ownProvider?.Dispose();
                }
            });

            CollectBetweenPhases();
            return (phase, exported, files.Count, stopwatch.Elapsed);
        }

        /// <summary>
        /// Exports every released map as a scene into the map export directory, on the main provider.
        /// A new MapExporter per world, so its caches don't grow from map to map.
        /// </summary>
        private static (string, int, int, TimeSpan) RunMapPhase(DefaultFileProvider provider, ExportSettings settings)
        {
            Failures.Phase = "maps";
            var stopwatch = Stopwatch.StartNew();
            string outputPath = Path.Combine(settings.ExportOutputPath!, WrfConstants.MapExportDirectory);

            List<string> mapPackages;
            try
            {
                mapPackages = MapList.ReleasedMapPackages(provider);
            }
            catch (Exception ex)
            {
                Failures.Record("map list", ex);
                return ("maps", 0, 0, stopwatch.Elapsed);
            }
            Console.WriteLine($"Phase maps: {mapPackages.Count} released map(s): {string.Join(", ", mapPackages.Select(Path.GetFileName))}");

            var packages = new HashSet<string>(provider.Files.Values.Select(file => file.ToString()), StringComparer.OrdinalIgnoreCase);
            int exported = 0;
            foreach (var mapPackage in mapPackages)
            {
                if (!packages.Contains(mapPackage + ".umap"))
                {
                    Failures.Record(mapPackage, "released map has no .umap in the game files");
                    continue;
                }

                var exporter = new MapExporter(provider, outputPath, new MapExportOptions { IsLoggingEnabled = settings.IsLoggingEnabled });
                if (exporter.ExportWorld(mapPackage))
                    exported++;
                CollectBetweenPhases();
            }
            return ("maps", exported, mapPackages.Count, stopwatch.Elapsed);
        }

        /// <summary>The game's .ini config files, which define the collision profiles and channels the scenes refer to.</summary>
        private static (string, int, int, TimeSpan) RunConfigPhase(DefaultFileProvider provider, ExportSettings settings)
        {
            Failures.Phase = "config";
            var stopwatch = Stopwatch.StartNew();
            string outputPath = Path.Combine(settings.ExportOutputPath!, WrfConstants.MapExportDirectory, "config");

            var configFiles = provider.Files.Values.Where(file => file.ToString().EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToList();
            int exported = 0;
            foreach (var file in configFiles)
            {
                string path = file.ToString();
                try
                {
                    string destinationFilePath = Path.Combine(outputPath, path);
                    CreateNeededDirectories(destinationFilePath);
                    File.WriteAllBytes(destinationFilePath, file.Read());
                    exported++;
                }
                catch (Exception ex)
                {
                    Failures.Record(path, ex);
                    Utils.LogInfo($"Failed to export config file {path}: {ex.Message}", settings.IsLoggingEnabled);
                }
            }
            return ("config", exported, configFiles.Count, stopwatch.Elapsed);
        }

        public static int Main(string[] args)
        {
            return new CommandApp<ExportCommand>().Run(args);
        }

        public static int Export(ExportSettings settings)
        {
            Failures.AbortOnOutOfMemory();

            // The GC heap hard limit when one is set (75% of a cgroup MemoryMax), otherwise physical RAM.
            long budget = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            Console.WriteLine($"Memory budget: {budget / (1024 * 1024)} MB");

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

            var allFiles = fileProvider.Files.Values.Select(file => file.ToString()).ToList();
            var assetFiles = allFiles
                .Where(path => ShouldProcessFile(path, exportDirectoriesToProcess))
                .Select(path => path.EndsWith(".locres", StringComparison.OrdinalIgnoreCase) ? path : path.Replace(".uasset", ""))
                .ToList();
            var meshFiles = assetFiles.Where(IsMeshAsset).ToList();
            var otherFiles = assetFiles.Where(path => !IsMeshAsset(path)).ToList();

            int assetWorkers = WorkersFor(budget, WrfConstants.AssetWorkerBytes);
            int meshWorkers = WorkersFor(budget, WrfConstants.MeshWorkerBytes);
            Console.WriteLine($"Workers: assets {assetWorkers}, meshes {meshWorkers}, maps 1");

            // Backstop for a mesh that the name prefix missed and that lands in the asset phase.
            AssetExporter.MeshGeometrySlots = new SemaphoreSlim(meshWorkers);

            var timings = new List<(string Phase, int Exported, int Total, TimeSpan Time)>();
            timings.Add(RunAssetPhase("assets", otherFiles, assetWorkers, fileProvider, settings));
            timings.Add(RunAssetPhase("meshes", meshFiles, meshWorkers, fileProvider, settings));
            timings.Add(RunMapPhase(fileProvider, settings));
            timings.Add(RunConfigPhase(fileProvider, settings));

            foreach (var (phase, exported, total, time) in timings)
                Console.WriteLine($"Phase {phase}: {exported}/{total} in {time:mm\\:ss}");

            return Failures.Summarize();
        }
    }
}
