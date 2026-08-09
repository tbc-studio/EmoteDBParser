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
        ("EmoteWheel_icon_", 4)
    };

    private static readonly Lazy<bool> NativeLibraries = new(() =>
    {
        if (!DetexHelper.LoadDll())
            return false;

        DetexHelper.Initialize("Detex.dll");
        OodleHelper.Initialize();
        return true;
    });

    private sealed class IconIndex
    {
        public Dictionary<int, string> ById { get; } = new();
        public Dictionary<string, List<string>> ByToken { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> AllPaths { get; } = new();
    }

    public static List<EmoteRow> ExportPubgAssets(
        string pakDirectory,
        string outputDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(pakDirectory))
            throw new DirectoryNotFoundException(
                $"Pak directory not found: {pakDirectory}");

        progress?.Report("Initializing native libraries...");

        if (!NativeLibraries.Value)
            throw new InvalidOperationException(
                "Failed to load Detex.dll.");

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Initializing file provider...");

        using var provider = new DefaultFileProvider(
            pakDirectory,
            SearchOption.AllDirectories,
            new VersionContainer(EGame.GAME_PlayerUnknownsBattlegrounds));

        provider.Initialize();
        provider.SubmitKey(
            new FGuid(),
            new FAesKey(AesKeyHex));

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(outputDirectory);

        var result = new ExportResult();

        progress?.Report("Indexing icon files...");
        var iconIndex = BuildIconIndex(provider);

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Exporting localization table...");

        if (!provider.TryGetGameFile(LocresPath, out var locresFile))
            throw new FileNotFoundException(
                $"Could not find {LocresPath} in provider");

        var locresData = locresFile.Read();
        var locres = new FTextLocalizationResource(
            new FByteArchive(LocresPath, locresData));

        string locresJsonPath =
            Path.Combine(outputDirectory, "TSL_Item.locres.json");

        File.WriteAllText(
            locresJsonPath,
            JsonConvert.SerializeObject(locres, Formatting.Indented));

        result.LocresJsonPath = locresJsonPath;

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Exporting EmoteDB data table...");

        if (!provider.TryGetGameFile(EmoteDbUassetPath, out _))
            throw new FileNotFoundException(
                $"Could not find {EmoteDbUassetPath} in provider");

        if (!provider.TrySavePackage(
                EmoteDbUassetPath,
                out var savedFiles))
        {
            throw new InvalidDataException(
                "Failed to save EmoteDB package.");
        }

        foreach (var (path, data) in savedFiles)
        {
            string outPath =
                Path.Combine(outputDirectory, Path.GetFileName(path));

            File.WriteAllBytes(outPath, data);

            if (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                result.UassetPath = outPath;
            else if (path.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase))
                result.UexpPath = outPath;
        }

        if (string.IsNullOrEmpty(result.UassetPath) ||
            string.IsNullOrEmpty(result.UexpPath))
        {
            throw new InvalidDataException(
                "Failed to export EmoteDB .uasset/.uexp pair.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report("Parsing emote rows...");

        string parsedJsonPath =
            Path.Combine(outputDirectory, "EmoteDB.json");

        var rows = Parser.Parse(
            result.UassetPath,
            result.UexpPath,
            parsedJsonPath,
            result.LocresJsonPath);

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report($"Exporting icons for {rows.Count} emotes...");

        string iconsDirectory =
            Path.Combine(outputDirectory, "Icons");

        Directory.CreateDirectory(iconsDirectory);

        int done = 0;
        int succeeded = 0;

        var failureReasons =
            new Dictionary<string, List<int>>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            iconIndex.ById.TryGetValue(
                row.Id,
                out var conventionPath);

            var (path, failureReason) = TryExportIconPng(
                provider,
                iconIndex,
                row.Id,
                row.Icon,
                conventionPath,
                iconsDirectory);

            row.IconImagePath = path;

            if (path != null)
                succeeded++;
            else
                Note(
                    failureReasons,
                    failureReason ?? "unknown",
                    row.Id);

            done++;

            if (done % 10 == 0 || done == rows.Count)
            {
                progress?.Report(
                    $"Exporting icons... ({done}/{rows.Count})");
            }
        }

        foreach (var (reason, ids) in failureReasons
                     .OrderByDescending(kv => kv.Value.Count))
        {
            string sample =
                string.Join(", ", ids.Take(10));

            string more =
                ids.Count > 10
                    ? $" (+{ids.Count - 10} more)"
                    : "";

            Console.WriteLine(
                $"  {ids.Count}x \"{reason}\" - rows: {sample}{more}");
        }

        progress?.Report(
            $"Done. Icons: {succeeded}/{rows.Count}");

        return rows;
    }

    private static IconIndex BuildIconIndex(
        DefaultFileProvider provider)
    {
        var index = new IconIndex();
        var bestRank = new Dictionary<int, int>();

        foreach (var key in provider.Files.Keys)
        {
            if (key.IndexOf(
                    "EmoteWheel/Icons/",
                    StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            string path = StripExtension(key);
            string fileName =
                Path.GetFileNameWithoutExtension(key);

            index.AllPaths.Add(path);

            if (TryMatchIconFileName(
                    fileName,
                    out int rank,
                    out int id))
            {
                if (!bestRank.TryGetValue(
                        id,
                        out int currentRank) ||
                    rank < currentRank)
                {
                    bestRank[id] = rank;
                    index.ById[id] = path;
                }
            }

            foreach (string token in GetTokens(fileName))
            {
                if (!index.ByToken.TryGetValue(
                        token,
                        out var paths))
                {
                    paths = new List<string>();
                    index.ByToken[token] = paths;
                }

                paths.Add(path);
            }
        }

        return index;
    }

    private static bool TryMatchIconFileName(
        string fileName,
        out int rank,
        out int id)
    {
        foreach (var (prefix, prefixRank)
                 in IconPrefixesInPriorityOrder)
        {
            if (!fileName.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int position = prefix.Length;
            int start = position;

            while (position < fileName.Length &&
                   char.IsDigit(fileName[position]))
            {
                position++;
            }

            if (position <= start)
                continue;

            if (int.TryParse(
                    fileName.AsSpan(start, position - start),
                    out int parsedId))
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

    private static void Note(
        Dictionary<string, List<int>> failureReasons,
        string reason,
        int rowId)
    {
        if (!failureReasons.TryGetValue(
                reason,
                out var ids))
        {
            ids = new List<int>();
            failureReasons[reason] = ids;
        }

        ids.Add(rowId);
    }

    private static (
        string? Path,
        string? FailureReason) TryExportIconPng(
        DefaultFileProvider provider,
        IconIndex index,
        int rowId,
        string? iconAssetPath,
        string? conventionPath,
        string outputDirectory)
    {
        var candidates =
            new List<string>(16);

        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            path = StripExtension(path);

            if (seen.Add(path))
                candidates.Add(path);
        }

        Add(iconAssetPath);
        Add(conventionPath);

        foreach (string path in FindIconGuesses(
                     index,
                     rowId,
                     iconAssetPath))
        {
            Add(path);
        }

        foreach (string path in GuessPathVariants(
                     iconAssetPath))
        {
            Add(path);
        }

        if (candidates.Count == 0)
        {
            return (
                null,
                "no icon path on row and no icon filename guess found");
        }

        string? lastFailure = null;

        foreach (string candidate in candidates)
        {
            try
            {
                if (!provider.TryGetGameFile(
                        candidate,
                        out _))
                {
                    lastFailure =
                        "asset file not found in mounted paks";
                    continue;
                }

                if (!provider.TryLoadPackageObject<UTexture2D>(
                        candidate,
                        out var texture) ||
                    texture == null)
                {
                    lastFailure =
                        "package loaded but no UTexture2D export with that name";
                    continue;
                }

                var decoded =
                    texture.Decode(
                        ETexturePlatform.DesktopMobile);

                if (decoded == null)
                {
                    lastFailure =
                        $"Decode() returned null (pixel format: {texture.Format})";
                    continue;
                }

                var imageData =
                    decoded.Encode(
                        ETextureFormat.Png,
                        false,
                        out var ext);

                if (imageData.Length == 0)
                {
                    lastFailure =
                        "Encode() returned empty data";
                    continue;
                }

                string fileName =
                    SanitizeFileName(
                        Path.GetFileNameWithoutExtension(
                            candidate)) +
                    "." +
                    ext;

                string outPath =
                    Path.Combine(
                        outputDirectory,
                        fileName);

                File.WriteAllBytes(
                    outPath,
                    imageData);

                return (outPath, null);
            }
            catch (Exception ex)
            {
                lastFailure =
                    $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        return (
            null,
            lastFailure ?? "icon not found in mounted paks");
    }

    private static IEnumerable<string> FindIconGuesses(
        IconIndex index,
        int rowId,
        string? originalIconPath)
    {
        var scores =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        void Add(string path, int score)
        {
            if (scores.TryGetValue(
                    path,
                    out int existing))
            {
                if (score > existing)
                    scores[path] = score;
            }
            else
            {
                scores[path] = score;
            }
        }

        if (index.ById.TryGetValue(
                rowId,
                out string? exact))
        {
            Add(exact, 1500);
        }

        if (!string.IsNullOrWhiteSpace(
                originalIconPath))
        {
            string originalName =
                Path.GetFileNameWithoutExtension(
                    originalIconPath);

            foreach (string token in GetTokens(
                         originalName))
            {
                if (!index.ByToken.TryGetValue(
                        token,
                        out var paths))
                {
                    continue;
                }

                foreach (string path in paths)
                    Add(path, 50);
            }
        }

        foreach (var candidate in scores
                     .OrderByDescending(x => x.Value)
                     .ThenBy(
                         x => x.Key,
                         StringComparer.OrdinalIgnoreCase)
                     .Take(30))
        {
            yield return candidate.Key;
        }
    }

    private static IEnumerable<string> GetTokens(
        string value)
    {
        int start = -1;

        for (int i = 0; i <= value.Length; i++)
        {
            bool separator =
                i == value.Length ||
                !char.IsLetterOrDigit(value[i]);

            if (separator)
            {
                if (start >= 0)
                {
                    int length = i - start;

                    if (length >= 3)
                    {
                        yield return value
                            .Substring(start, length)
                            .ToLowerInvariant();
                    }

                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }
    }

    private static IEnumerable<string> GuessPathVariants(
        string? iconAssetPath)
    {
        if (string.IsNullOrWhiteSpace(iconAssetPath))
            yield break;

        string clean =
            StripExtension(iconAssetPath);

        if (clean.StartsWith(
                "/Game/",
                StringComparison.OrdinalIgnoreCase))
        {
            clean =
                "TslGame/Content/" +
                clean["/Game/".Length..];
        }

        int slashIndex =
            clean.LastIndexOf('/');

        string directory =
            slashIndex >= 0
                ? clean[..(slashIndex + 1)]
                : "";

        string name =
            Path.GetFileName(clean);

        if (string.IsNullOrWhiteSpace(name))
            yield break;

        var names = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            name,
            name + "_dance",
            name + "_01",
            name + "_02",
            name + "_a",
            name + "_b"
        };

        if (name.StartsWith(
                "emote_creative_icon_",
                StringComparison.OrdinalIgnoreCase))
        {
            string suffix =
                name["emote_creative_icon_".Length..];

            names.Add("emote_icon_" + suffix);
            names.Add("test_emote_icon_" + suffix);
            names.Add("movie_emote_icon_" + suffix);
        }
        else if (name.StartsWith(
                     "emote_icon_",
                     StringComparison.OrdinalIgnoreCase))
        {
            string suffix =
                name["emote_icon_".Length..];

            names.Add("emote_creative_icon_" + suffix);
            names.Add("test_emote_icon_" + suffix);
            names.Add("movie_emote_icon_" + suffix);
        }

        foreach (string candidateName in names)
            yield return directory + candidateName;
    }

    private static string SanitizeFileName(
        string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }

    private static string StripExtension(
        string path)
    {
        int dot =
            path.LastIndexOf('.');

        int slash =
            path.LastIndexOf('/');

        return dot > slash
            ? path[..dot]
            : path;
    }
}
