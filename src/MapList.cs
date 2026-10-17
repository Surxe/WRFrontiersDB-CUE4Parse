using CUE4Parse.FileProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BatchExport
{
    /// <summary>
    /// The maps to export, read from the game's own list. The matchmaking data names every map a
    /// queue can pick (MapsLocalization, keyed by the map's folder), and ST_MapNames also names the
    /// ones no queue picks. The firing range is in neither, so WrfConstants adds it.
    /// </summary>
    public static class MapList
    {
        /// <summary>The persistent level package of every released map, queued maps first in the game's order.</summary>
        public static List<string> ReleasedMapPackages(DefaultFileProvider provider)
        {
            var keys = QueuedMapKeys(provider);
            foreach (var key in NamedMapKeys(provider).Concat(WrfConstants.UnnamedMaps))
            {
                if (!keys.Contains(key))
                    keys.Add(key);
            }
            return keys.Select(key => $"{WrfConstants.MapsRoot}/{key}/{key}").ToList();
        }

        private static List<string> QueuedMapKeys(DefaultFileProvider provider)
        {
            var asset = LoadAsJson(provider, WrfConstants.QueueCommonDataPackage)
                .FirstOrDefault(entry => (string?)entry["Type"] == WrfConstants.QueueCommonDataType)
                ?? throw new InvalidDataException($"No {WrfConstants.QueueCommonDataType} in {WrfConstants.QueueCommonDataPackage}");

            var keys = (asset["Properties"]?["MapsLocalization"] as JArray ?? [])
                .Select(item => (string?)item["Key"])
                .Where(key => !string.IsNullOrEmpty(key))
                .Select(key => key!)
                .ToList();

            if (keys.Count == 0)
                throw new InvalidDataException($"No maps in MapsLocalization of {WrfConstants.QueueCommonDataPackage}");
            return keys;
        }

        private static IEnumerable<string> NamedMapKeys(DefaultFileProvider provider)
        {
            var table = LoadAsJson(provider, WrfConstants.MapNamesPackage)
                .FirstOrDefault(entry => (string?)entry["Type"] == "StringTable")
                ?? throw new InvalidDataException($"No StringTable in {WrfConstants.MapNamesPackage}");

            var entries = table["StringTable"]?["KeysToEntries"] as JObject ?? [];
            return entries.Properties()
                .Select(entry => entry.Name)
                .Where(key => key.StartsWith(WrfConstants.MapNameKeyPrefix, StringComparison.Ordinal))
                .Select(key => key[WrfConstants.MapNameKeyPrefix.Length..]);
        }

        /// <summary>A package's exports as the same JSON the asset export writes.</summary>
        private static JArray LoadAsJson(DefaultFileProvider provider, string packagePath)
        {
            var package = provider.LoadPackage(packagePath);
            return JArray.Parse(JsonConvert.SerializeObject(package.GetExports()));
        }
    }
}
