using CUE4Parse_Conversion;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Readers;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Textures.BC;
using EmoteDBParser;
using Newtonsoft.Json;
using OodleDotNet;
using OodleSharp;

public class ExportResult
{
    public string LocresJsonPath { get; set; } = "";
    public string UassetPath { get; set; } = "";
    public string UexpPath { get; set; } = "";
}

public static class Initializer
{
    private const string AesKeyHex =
        "0x3435444431354436444432444135304145423731434537413532383443463845";

    private const string LocresPath =
        "TslGame/Content/Localization/TSL_Item/en/TSL_Item.locres";

    private const string EmoteDbUassetPath =
        "TslGame/Content/Animations/Battlegrounds/Anims/Emotes/EmoteDB.uasset";

    private static readonly (string Prefix, int Rank)[] IconPrefixesInPriorityOrder =
    {
        ("emote_icon_", 0),
        ("emote_creative_icon_", 1),
        ("test_emote_icon_", 2),
        ("movie_emote_icon_", 3),
        ("EmoteWheel_icon_", 4),
    };
    
    public static List<EmoteRow> ExportPubgAssets(
        string pakDirectory,
        string outputDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(pakDirectory))
            throw new DirectoryNotFoundException($"Pak directory not found: {pakDirectory}");


        progress?.Report("Initializing Detex...");

        if (!DetexHelper.LoadDll())
            throw new InvalidOperationException("Failed to load Detex.dll.");

        DetexHelper.Initialize("Detex.dll");

        progress?.Report("Initializing Oodle...");
        OodleHelper.Initialize();

        progress?.Report("Initializing file provider...");

        using var provider = new DefaultFileProvider(
            pakDirectory,
            SearchOption.AllDirectories,
            new VersionContainer(EGame.GAME_PlayerUnknownsBattlegrounds)
        );

        provider.Initialize();

        var aesKey = new FAesKey(AesKeyHex);
        provider.SubmitKey(new FGuid(), aesKey);

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);

        var result = new ExportResult();

        progress?.Report("Exporting localization table...");

        if (provider.TryGetGameFile(LocresPath, out var locresFile))
        {
            var locresData = locresFile.Read();
            var locres = new FTextLocalizationResource(new FByteArchive(LocresPath, locresData));

            string locresJson = JsonConvert.SerializeObject(locres, Formatting.Indented);
            string locresJsonPath = Path.Combine(outputDirectory, "TSL_Item.locres.json");
            File.WriteAllText(locresJsonPath, locresJson);

            result.LocresJsonPath = locresJsonPath;
        }
        else
        {
            throw new FileNotFoundException($"Could not find {LocresPath} in provider");
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Exporting EmoteDB data table...");

        if (provider.TryGetGameFile(EmoteDbUassetPath, out _))
        {
            if (provider.TrySavePackage(EmoteDbUassetPath, out var savedFiles))
            {
                foreach (var (path, data) in savedFiles)
                {
                    string outPath = Path.Combine(outputDirectory, Path.GetFileName(path));
                    File.WriteAllBytes(outPath, data);

                    if (path.EndsWith(".uasset"))
                        result.UassetPath = outPath;
                    else if (path.EndsWith(".uexp"))
                        result.UexpPath = outPath;
                }
            }
        }
        else
        {
            throw new FileNotFoundException($"Could not find {EmoteDbUassetPath} in provider");
        }

        if (string.IsNullOrEmpty(result.UassetPath) || string.IsNullOrEmpty(result.UexpPath))
            throw new InvalidDataException("Failed to export EmoteDB .uasset/.uexp pair.");

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Parsing emote rows...");

        string parsedJsonPath = Path.Combine(outputDirectory, "EmoteDB.json");

        var rows = Parser.Parse(
            result.UassetPath,
            result.UexpPath,
            parsedJsonPath,
            result.LocresJsonPath
        );

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report($"Exporting icons for {rows.Count} emotes...");

        string iconsDirectory = Path.Combine(outputDirectory, "Icons");
        Directory.CreateDirectory(iconsDirectory);

        progress?.Report("Indexing icon files...");

        var iconIndex = BuildIconIndex(provider);

        int done = 0;
        int succeeded = 0;
        var failureReasons = new Dictionary<string, List<int>>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            iconIndex.TryGetValue(row.Id, out var conventionPath);

            var (path, failureReason) = TryExportIconPng(
                provider,
                row.Icon,
                conventionPath,
                iconsDirectory
            );

            row.IconImagePath = path;

            if (path != null)
                succeeded++;
            else
                Note(failureReasons, failureReason ?? "unknown", row.Id);

            done++;

            if (done % 5 == 0 || done == rows.Count)
                progress?.Report($"Exporting icons... ({done}/{rows.Count})");
        }

        Console.WriteLine($"Icon export: {succeeded}/{rows.Count} succeeded.");

        foreach (var (reason, ids) in failureReasons.OrderByDescending(kv => kv.Value.Count))
        {
            string sample = string.Join(", ", ids.Take(10));
            string more = ids.Count > 10 ? $" (+{ids.Count - 10} more)" : "";

            Console.WriteLine($"  {ids.Count}x \"{reason}\" - rows: {sample}{more}");
        }

        progress?.Report("Done.");

        return rows;
    }
    
    private static Dictionary<int, string> BuildIconIndex(DefaultFileProvider provider)
    {
        var index = new Dictionary<int, string>();
        var bestRank = new Dictionary<int, int>();

        foreach (var key in provider.Files.Keys)
        {
            if (key.IndexOf("EmoteWheel/Icons/", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string fileName = Path.GetFileNameWithoutExtension(key);

            if (!TryMatchIconFileName(fileName, out int rank, out int id))
                continue;

            if (!bestRank.TryGetValue(id, out int currentRank) || rank < currentRank)
            {
                bestRank[id] = rank;
                index[id] = StripExtension(key);
            }
        }

        Console.WriteLine($"Indexed {index.Count} EmoteWheel icon files.");

        return index;
    }
    
    private static bool TryMatchIconFileName(string fileName, out int rank, out int id)
    {
        foreach (var (prefix, prefixRank) in IconPrefixesInPriorityOrder)
        {
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string rest = fileName[prefix.Length..];

            int digitCount = 0;
            while (digitCount < rest.Length && char.IsDigit(rest[digitCount]))
                digitCount++;

            if (digitCount > 0 &&
                int.TryParse(rest[..digitCount], out int parsedId))
            {
                rank = prefixRank;
                id = parsedId;
                return true;
            }
        }

        rank = -1;
        id = -1;
        return false;
    }

    private static void Note(Dictionary<string, List<int>> failureReasons, string reason, int rowId)
    {
        if (!failureReasons.TryGetValue(reason, out var ids))
        {
            ids = new List<int>();
            failureReasons[reason] = ids;
        }

        ids.Add(rowId);
    }
    
    private static (string? Path, string? FailureReason) TryExportIconPng(
        DefaultFileProvider provider,
        string? iconAssetPath,
        string? conventionPath,
        string outputDirectory)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(iconAssetPath))
            candidates.Add(iconAssetPath);

        if (!string.IsNullOrWhiteSpace(conventionPath) &&
            !candidates.Any(x => string.Equals(x, conventionPath, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.Add(conventionPath);
        }

        if (candidates.Count == 0)
            return (null, "no icon path on row and none found in mounted paks");

        string? lastFailure = null;

        foreach (string candidate in candidates)
        {
            try
            {
                if (!provider.TryGetGameFile(candidate, out _))
                {
                    lastFailure = "asset file not found in mounted paks";
                    continue;
                }

                if (!provider.TryLoadPackageObject<UTexture2D>(candidate, out var texture) || texture == null)
                {
                    lastFailure = "package loaded but no UTexture2D export with that name";
                    continue;
                }

                var decoded = texture.Decode(ETexturePlatform.DesktopMobile);
                
                if (decoded == null)
                {
                    lastFailure = $"Decode() returned null (pixel format: {texture.Format})";
                    continue;
                }
                
                var imageData = decoded.Encode(ETextureFormat.Png, false, out var ext);

                if (imageData.Length == 0)
                {
                    lastFailure = "Encode() returned empty data";
                    continue;
                }

                string fileName =
                    SanitizeFileName(Path.GetFileNameWithoutExtension(candidate)) + "." + ext;
                string outPath = Path.Combine(outputDirectory, fileName);

                File.WriteAllBytes(outPath, imageData);

                return (outPath, null);
            }
            catch (Exception ex)
            {
                lastFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        return (null, lastFailure ?? "icon not found in mounted paks");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }
    
    private static string StripExtension(string path)
    {
        int dot = path.LastIndexOf('.');
        int slash = path.LastIndexOf('/');

        return dot > slash ? path[..dot] : path;
    }
}