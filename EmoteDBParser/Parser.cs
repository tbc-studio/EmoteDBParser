using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace EmoteDBParser;
public sealed class EmoteRow
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("row")]
    public string Row { get; set; } = "";
    [JsonPropertyName("nameKey")]
    public string? NameKey { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("nameFrom")]
    public string? NameFrom { get; set; }
    [JsonPropertyName("nameTable")]
    public string? NameTable { get; set; }
    [JsonPropertyName("playType")]
    public string PlayType { get; set; } = "Unknown";
    [JsonPropertyName("moveType")]
    public string MoveType { get; set; } = "None";
    [JsonPropertyName("maxMoveSpeed")]
    public float MaxMoveSpeed { get; set; }
    [JsonPropertyName("collisionRadius")]
    public float CollisionRadius { get; set; }
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
    [JsonPropertyName("iconImagePath")]
    public string? IconImagePath { get; set; }
    [JsonPropertyName("montage")]
    public string? Montage { get; set; }
    [JsonPropertyName("montageFound")]
    public bool MontageFound { get; set; }
    [JsonPropertyName("unused")]
    public bool Unused { get; set; }
    [JsonPropertyName("durationSeconds")]
    public float? DurationSeconds { get; set; }
    [JsonPropertyName("skeleton")]
    public string? Skeleton { get; set; }
    [JsonPropertyName("sequences")]
    public string[] Sequences { get; set; } = Array.Empty<string>();
    [JsonPropertyName("sections")]
    public object[] Sections { get; set; } = Array.Empty<object>();
    [JsonPropertyName("Localization")]
    public string? Localization { get; set; }
}
public static class Parser
{
    private const uint PackageTag = 0x9E2A83C1;
    private const int EmoteIdName = 1050;
    private const int PlayTypeName = 1099;
    private const int MoveTypeName = 1089;
    private const int MaxMoveSpeedName = 1088;
    private const int CollisionRadiusName = 1091;
    private const int LocalizedNameName = 1054;
    private const int UITextureName = 1125;
    private const int RandomEmoteIdsName = 1100;
    private const int ParticipantsName = 1053;
    private const int EnumProperty = 1067;
    private const int IntProperty = 1070;
    private const int FloatProperty = 1068;
    public static List<EmoteRow> Parse(
        string uassetPath,
        string uexpPath,
        string outputPath,
        string localizationPath)
    {
        byte[] uasset = File.ReadAllBytes(uassetPath);
        byte[] uexp = File.ReadAllBytes(uexpPath);
        uexp = RepairReplacementBytes(uexp);
        var package = ParsePackage(uasset);
        if (package.ExportCount != 1)
            throw new InvalidDataException(
                $"Expected one export, got {package.ExportCount}."
            );
        int exportOffset = package.ExportOffset;
        int serialSize = ReadInt32(
            uasset,
            exportOffset + 0x1C
        );
        int serialOffset = ReadInt32(
            uasset,
            exportOffset + 0x20
        );
        var localizationNamespaces = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
            File.ReadAllText(localizationPath)
        ) ?? new Dictionary<string, Dictionary<string, string>>();
        var localization = new Dictionary<string, string>();
        foreach (var ns in localizationNamespaces.Values)
        {
            foreach (var kvp in ns)
            {
                localization[kvp.Key] = kvp.Value;
            }
        }
        Console.WriteLine($"SerialSize:   {serialSize}");
        Console.WriteLine($"SerialOffset: {serialOffset}");
        Console.WriteLine($"UEXP size:    {uexp.Length}");
        if (serialSize < 0 || serialSize > uexp.Length)
        {
            throw new InvalidDataException(
                $"Invalid serial size: {serialSize}, UEXP size: {uexp.Length}"
            );
        }
        var emoteIdTag = FNamePair(EmoteIdName);
        var rowStarts = FindAll(
            uexp,
            emoteIdTag
        );
        Console.WriteLine(
            $"Found {rowStarts.Count} EmoteID properties."
        );
        var rows = new List<EmoteRow>();
        int serialEnd = serialSize;
        for (int i = 0; i < rowStarts.Count; i++)
        {
            int rowStart = rowStarts[i];
            int rowEnd = i + 1 < rowStarts.Count
                ? rowStarts[i + 1]
                : serialEnd;
            try
            {
                var row = ParseRow(
                    uasset,
                    uexp,
                    package,
                    rowStart,
                    rowEnd
                );
                if (row.NameKey is string nameKey)
                {
                    string? localizedName = null;
                    if (row.NameTable is string nameTable &&
                        localizationNamespaces.TryGetValue(nameTable, out var ns) &&
                        ns.TryGetValue(nameKey, out var scopedName))
                    {
                        localizedName = scopedName;
                    }
                    else if (localization.TryGetValue(nameKey, out var flatName))
                    {
                        localizedName = flatName;
                    }
                    if (localizedName != null)
                    {
                        row.Name = localizedName;
                    }
                    row.Localization = localizedName;
                }
                else
                {
                    row.Localization = null;
                }
                rows.Add(row);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Failed to parse row at 0x{rowStart:X}: {ex.Message}"
                );
            }
        }
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        string json = JsonSerializer.Serialize(rows, options);
        File.WriteAllText(
            outputPath,
            json,
            new UTF8Encoding(false)
        );
        Console.WriteLine(
            $"Wrote {rows.Count} rows to {outputPath}"
        );
        return rows;
    }
    private static EmoteRow ParseRow(
        byte[] uasset,
        byte[] uexp,
        PackageInfo package,
        int rowStart,
        int rowEnd)
    {
        var rowNameFName = ReadFName(
            uexp,
            rowStart - 8,
            package.Names
        );
        string rowName = rowNameFName.Name;
        int id = ExtractId(rowName);
        var result = new EmoteRow
        {
            Id = id,
            Row = rowName
        };
        int localizedTag = FindTag(
            uexp,
            rowStart,
            rowEnd,
            LocalizedNameName
        );
        if (localizedTag >= 0)
        {
            ParseLocalizedName(
                uexp,
                localizedTag,
                rowEnd,
                result
            );
        }
        else
        {
            result.Name = rowName;
            result.NameFrom = "source";
        }
        int playTag = FindTag(
            uexp,
            rowStart,
            rowEnd,
            PlayTypeName
        );
        if (playTag >= 0)
        {
            string playType = ReadEnumValue(
                uexp,
                playTag,
                package.Names
            );
            result.PlayType = playType;
        }
        else
        {
            result.PlayType = "Unknown";
        }
        int moveTag = FindTag(
            uexp,
            rowStart,
            rowEnd,
            MoveTypeName
        );
        if (moveTag >= 0)
        {
            result.MoveType = ReadEnumValue(
                uexp,
                moveTag,
                package.Names
            );
        }
        else
        {
            result.MoveType = "None";
        }
        int maxSpeedTag = FindTag(
            uexp,
            rowStart,
            rowEnd,
            MaxMoveSpeedName
        );
        result.MaxMoveSpeed =
            maxSpeedTag >= 0
                ? ReadFloatProperty(uexp, maxSpeedTag)
                : 0.0f;
        int collisionTag = FindTag(
            uexp,
            rowStart,
            rowEnd,
            CollisionRadiusName
        );
        result.CollisionRadius =
            collisionTag >= 0
                ? ReadFloatProperty(uexp, collisionTag)
                : 0.0f;
        int iconTag = FindTag(
            uexp,
            rowStart,
            rowEnd,
            UITextureName
        );
        result.Icon = iconTag >= 0
            ? ReadObjectPropertyPath(uexp, iconTag, package)
            : null;
        result.Icon ??= GuessIcon(
            package.Names,
            id
        );
        result.Icon ??= GetFallbackIconPath(id);
        result.Montage = FindMontagePath(
            uexp,
            rowStart,
            rowEnd
        );
        result.MontageFound =
            result.Montage != null;
        result.Unused = false;
        result.DurationSeconds = null;
        result.Skeleton = null;
        result.Sequences = Array.Empty<string>();
        result.Sections = Array.Empty<object>();
        return result;
    }
    private static void ParseLocalizedName(
        byte[] data,
        int tag,
        int rowEnd,
        EmoteRow result)
    {
        int valueStart = tag + 25;
        int length = Math.Max(0, rowEnd - valueStart);
        string[] strings = ExtractAsciiStrings(
            data,
            valueStart,
            length
        );
        string? nameKey = null;
        string? name = null;
        foreach (string s in strings)
        {
            if (Regex.IsMatch(
                    s,
                    "^[A-Fa-f0-9]{32}$"))
            {
                nameKey = s;
                continue;
            }
            if (s.StartsWith("K_", StringComparison.Ordinal))
            {
                nameKey = s;
                continue;
            }
            if (s.StartsWith("EEmote", StringComparison.Ordinal))
                continue;
            if (s is "source" or "locres")
                continue;
            if (s.Length >= 2 &&
                !s.StartsWith("/Game/", StringComparison.Ordinal))
            {
                name ??= s;
            }
        }
        if (nameKey != null)
        {
            result.NameKey = nameKey;
        }
        result.Name = name ?? result.Row;
        if (nameKey != null &&
            nameKey.StartsWith("K_", StringComparison.Ordinal))
        {
            result.NameFrom = "locres";
            result.NameTable = "NS_EmotesTitle";
        }
        else
        {
            result.NameFrom = "source";
        }
    }
    private static string ReadEnumValue(
        byte[] data,
        int tag,
        List<string> names)
    {
        int enumValueNameIndex =
            ReadInt32(data, tag + 33);
        if (enumValueNameIndex >= 0 &&
            enumValueNameIndex < names.Count)
        {
            string value = names[enumValueNameIndex];
            const string playPrefix = "EEmotePlayType::";
            const string movePrefix = "EEmoteMoveType::";
            if (value.StartsWith(playPrefix))
                return value[playPrefix.Length..];
            if (value.StartsWith(movePrefix))
                return value[movePrefix.Length..];
            return value;
        }
        return "Unknown";
    }
    private static float ReadFloatProperty(
        byte[] data,
        int tag)
    {
        return BitConverter.ToSingle(
            data,
            tag + 25
        );
    }
    private static string? ReadObjectPropertyPath(
        byte[] data,
        int tag,
        PackageInfo package)
    {
        if (tag + 29 > data.Length)
            return null;
        int packageIndex = ReadInt32(data, tag + 25);
        if (packageIndex >= 0)
            return null;
        int importIndex = -packageIndex;
        string? objectPath =
            ResolveImportObjectPath(
                package,
                importIndex,
                new HashSet<int>());
        return objectPath != null
            ? CleanAssetPath(objectPath)
            : null;
    }
    private static string? ResolveImportObjectPath(
        PackageInfo package,
        int importIndex,
        HashSet<int> visited)
    {
        if (importIndex <= 0 ||
            importIndex > package.Imports.Count ||
            !visited.Add(importIndex))
        {
            return null;
        }
        ImportInfo import = package.Imports[importIndex - 1];
        if (import.OuterIndex == 0)
        {
            string packageName = FormatFName(import.ObjectName, import.ObjectNumber);
            return packageName.StartsWith(
                       "/Game/",
                       StringComparison.OrdinalIgnoreCase)
                ? packageName
                : null;
        }
        if (import.OuterIndex < 0)
        {
            string? outerPath =
                ResolveImportObjectPath(
                    package,
                    -import.OuterIndex,
                    visited);
            if (outerPath == null)
                return null;
            return outerPath;
        }
        return null;
    }
    private static string FormatFName(string name, int number)
    {
        return number == 0 ? name : $"{name}_{number - 1}";
    }
    private static string? FindMontagePath(
        byte[] data,
        int rowStart,
        int rowEnd)
    {
        int start = rowStart;
        int length = rowEnd - rowStart;
        byte[] marker =
            Encoding.ASCII.GetBytes("/Game/Animations/");
        int pos = IndexOf(
            data,
            marker,
            start,
            length
        );
        if (pos < 0)
            return null;
        int end = pos;
        while (end < rowEnd &&
               data[end] != 0)
        {
            end++;
        }
        string path = Encoding.UTF8.GetString(
            data,
            pos,
            end - pos
        );
        return CleanAssetPath(path);
    }
    private static readonly string[] IconPrefixesInPriorityOrder =
    {
        "emote_icon_",
        "emote_creative_icon_",
        "test_emote_icon_",
        "movie_emote_icon_",
        "EmoteWheel_icon_",
    };
    private static string? GuessIcon(
        List<string> names,
        int id)
    {
        string plain = id.ToString();
        string padded = id.ToString("00");
        foreach (string prefix in IconPrefixesInPriorityOrder)
        {
            foreach (string idText in plain == padded
                         ? new[] { plain }
                         : new[] { plain, padded })
            {
                string fullPrefix =
                    $"/Game/UI/HUD/EmoteWheel/Icons/{prefix}{idText}";
                string? match = names.FirstOrDefault(x =>
                    x.StartsWith(fullPrefix, StringComparison.OrdinalIgnoreCase) &&
                    IsIdBoundary(x, fullPrefix.Length));
                if (match != null)
                    return CleanAssetPath(match);
            }
        }
        return null;
    }
    private static bool IsIdBoundary(string name, int indexAfterId)
    {
        return indexAfterId >= name.Length ||
               !char.IsDigit(name[indexAfterId]);
    }
    private static string CleanAssetPath(string path)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith("/Game/",
                StringComparison.OrdinalIgnoreCase))
        {
            path =
                "TslGame/Content/" +
                path["/Game/".Length..];
        }
        int dot = path.LastIndexOf('.');
        if (dot > path.LastIndexOf('/'))
            path = path[..dot];
        return path;
    }
    private static int FindTag(
        byte[] data,
        int start,
        int end,
        int propertyNameIndex)
    {
        byte[] pattern =
            FNamePair(propertyNameIndex);
        int pos = IndexOf(
            data,
            pattern,
            start,
            end - start
        );
        return pos;
    }
    private static PackageInfo ParsePackage(
        byte[] data)
    {
        if (ReadUInt32(data, 0) != PackageTag)
            throw new InvalidDataException(
                "Not a valid Unreal package."
            );
        int legacyVersion = ReadInt32(data, 4);
        if (legacyVersion != -7)
        {
            Console.WriteLine(
                $"Warning: expected legacy version -7, got {legacyVersion}."
            );
        }
        int totalHeaderSize =
            ReadInt32(data, 24);
        int pos = 28;
        string packageName =
            ReadFString(
                data,
                ref pos
            );
        uint packageFlags =
            ReadUInt32(
                data,
                ref pos
            );
        int nameCount =
            ReadInt32(
                data,
                ref pos
            );
        int nameOffset =
            ReadInt32(
                data,
                ref pos
            );
        _ = ReadInt32(data, ref pos);
        _ = ReadInt32(data, ref pos);
        int exportCount =
            ReadInt32(
                data,
                ref pos
            );
        int exportOffset =
            ReadInt32(
                data,
                ref pos
            );
        int importCount =
            ReadInt32(
                data,
                ref pos
            );
        int importOffset =
            ReadInt32(
                data,
                ref pos
            );
        int dependsOffset =
            ReadInt32(
                data,
                ref pos
            );
        Console.WriteLine(
            $"Package: {packageName}"
        );
        Console.WriteLine(
            $"Names: {nameCount} @ 0x{nameOffset:X}"
        );
        Console.WriteLine(
            $"Imports: {importCount} @ 0x{importOffset:X}"
        );
        Console.WriteLine(
            $"Exports: {exportCount} @ 0x{exportOffset:X}"
        );
        var names = ReadNameMap(
            data,
            nameOffset,
            nameCount
        );
        var imports = ReadImportMap(
            data,
            importOffset,
            importCount,
            names
        );
        return new PackageInfo
        {
            TotalHeaderSize = totalHeaderSize,
            PackageFlags = packageFlags,
            NameCount = nameCount,
            NameOffset = nameOffset,
            ExportCount = exportCount,
            ExportOffset = exportOffset,
            ImportCount = importCount,
            ImportOffset = importOffset,
            DependsOffset = dependsOffset,
            Names = names,
            Imports = imports
        };
    }
    private static List<ImportInfo> ReadImportMap(
        byte[] data,
        int offset,
        int count,
        List<string> names)
    {
        var imports = new List<ImportInfo>(count);
        int pos = offset;
        const int importSize = 28;
        for (int i = 0; i < count; i++)
        {
            if (pos < 0 || pos + importSize > data.Length)
                throw new InvalidDataException(
                    $"Import table entry {i} is outside the .uasset.");
            string classPackage =
                ReadFName(data, pos, names).Name;
            string className =
                ReadFName(data, pos + 8, names).Name;
            int outerIndex =
                ReadInt32(data, pos + 16);
            var objectNameFName =
                ReadFName(data, pos + 20, names);
            string objectName = objectNameFName.Name;
            int objectNumber = objectNameFName.Number;
            imports.Add(
                new ImportInfo(
                    classPackage,
                    className,
                    outerIndex,
                    objectName,
                    objectNumber));
            pos += importSize;
        }
        return imports;
    }
    private static List<string> ReadNameMap(
        byte[] data,
        int offset,
        int count)
    {
        var names = new List<string>(
            count
        );
        int pos = offset;
        for (int i = 0; i < count; i++)
        {
            int length =
                ReadInt32(
                    data,
                    pos
                );
            pos += 4;
            string name;
            if (length > 0)
            {
                int byteCount = length;
                if (data[pos + byteCount - 1] == 0)
                    byteCount--;
                name = Encoding.UTF8.GetString(
                    data,
                    pos,
                    byteCount
                );
                pos += length;
            }
            else if (length < 0)
            {
                int chars = -length;
                int byteCount = chars * 2;
                name = Encoding.Unicode.GetString(
                    data,
                    pos,
                    byteCount
                ).TrimEnd('\0');
                pos += byteCount;
            }
            else
            {
                name = string.Empty;
            }
            pos += 4;
            names.Add(name);
        }
        return names;
    }
    private static FNameValue ReadFName(
        byte[] data,
        int offset,
        List<string> names)
    {
        int index =
            ReadInt32(data, offset);
        int number =
            ReadInt32(data, offset + 4);
        string name =
            index >= 0 && index < names.Count
                ? names[index]
                : $"<Name:{index}>";
        return new FNameValue(
            index,
            number,
            name
        );
    }
    private static string ReadFString(
        byte[] data,
        ref int offset)
    {
        int length =
            ReadInt32(data, offset);
        offset += 4;
        if (length == 0)
            return string.Empty;
        if (length > 0)
        {
            string value =
                Encoding.UTF8.GetString(
                    data,
                    offset,
                    length
                );
            offset += length;
            return value.TrimEnd('\0');
        }
        int chars = -length;
        string unicode =
            Encoding.Unicode.GetString(
                data,
                offset,
                chars * 2
            );
        offset += chars * 2;
        return unicode.TrimEnd('\0');
    }
    private static string[] ExtractAsciiStrings(
        byte[] data,
        int offset,
        int length)
    {
        if (length <= 0)
            return Array.Empty<string>();
        int end = Math.Min(
            data.Length,
            offset + length
        );
        var result = new List<string>();
        int start = -1;
        for (int i = offset; i < end; i++)
        {
            byte c = data[i];
            bool printable =
                c >= 0x20 &&
                c <= 0x7E;
            if (printable)
            {
                if (start < 0)
                    start = i;
            }
            else
            {
                if (start >= 0)
                {
                    int count = i - start;
                    if (count >= 3)
                    {
                        result.Add(
                            Encoding.ASCII.GetString(
                                data,
                                start,
                                count
                            )
                        );
                    }
                    start = -1;
                }
            }
        }
        if (start >= 0)
        {
            int count = end - start;
            if (count >= 3)
            {
                result.Add(
                    Encoding.ASCII.GetString(
                        data,
                        start,
                        count
                    )
                );
            }
        }
        return result.ToArray();
    }
    private static byte[] FNamePair(
        int index)
    {
        byte[] result = new byte[8];
        BitConverter.TryWriteBytes(
            result.AsSpan(0, 4),
            index
        );
        BitConverter.TryWriteBytes(
            result.AsSpan(4, 4),
            0
        );
        return result;
    }
    private static List<int> FindAll(
        byte[] data,
        byte[] pattern)
    {
        var result = new List<int>();
        int pos = 0;
        while (pos <= data.Length - pattern.Length)
        {
            int found =
                IndexOf(
                    data,
                    pattern,
                    pos,
                    data.Length - pos
                );
            if (found < 0)
                break;
            result.Add(found);
            pos = found + pattern.Length;
        }
        return result;
    }
    private static int IndexOf(
        byte[] data,
        byte[] pattern,
        int start,
        int length)
    {
        int end =
            Math.Min(
                data.Length,
                start + length
            );
        for (int i = start;
             i <= end - pattern.Length;
             i++)
        {
            bool match = true;
            for (int j = 0;
                 j < pattern.Length;
                 j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }
    private static byte[] RepairReplacementBytes(
        byte[] data)
    {
        const byte EF = 0xEF;
        const byte BF = 0xBF;
        const byte BD = 0xBD;
        if (!data.Contains(EF))
            return data;
        var output = new List<byte>(
            data.Length
        );
        for (int i = 0; i < data.Length;)
        {
            if (i + 2 < data.Length &&
                data[i] == EF &&
                data[i + 1] == BF &&
                data[i + 2] == BD)
            {
                output.Add(0xFF);
                i += 3;
                continue;
            }
            output.Add(data[i]);
            i++;
        }
        return output.ToArray();
    }
    private static string? ExtractLocalizationText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[]
                     {
                         "text",
                         "value",
                         "translation",
                         "localized",
                         "localizedText",
                         "source"
                     })
            {
                if (value.TryGetProperty(propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }
        }
        return null;
    }
    private static int ExtractId(
        string row)
    {
        Match match =
            Regex.Match(
                row,
                @"^Emote(\d+)$"
            );
        return match.Success
            ? int.Parse(match.Groups[1].Value)
            : -1;
    }
    private static int ReadInt32(
        byte[] data,
        int offset)
    {
        return BitConverter.ToInt32(
            data,
            offset
        );
    }
    private static uint ReadUInt32(
        byte[] data,
        int offset)
    {
        return BitConverter.ToUInt32(
            data,
            offset
        );
    }
    private static int ReadInt32(
        byte[] data,
        ref int offset)
    {
        int value =
            ReadInt32(data, offset);
        offset += 4;
        return value;
    }
    private static uint ReadUInt32(
        byte[] data,
        ref int offset)
    {
        uint value =
            ReadUInt32(data, offset);
        offset += 4;
        return value;
    }
    private static long ReadInt64(
        byte[] data,
        int offset)
    {
        return BitConverter.ToInt64(
            data,
            offset
        );
    }
    private sealed class PackageInfo
    {
        public int TotalHeaderSize { get; init; }
        public uint PackageFlags { get; init; }
        public int NameCount { get; init; }
        public int NameOffset { get; init; }
        public int ExportCount { get; init; }
        public int ExportOffset { get; init; }
        public int ImportCount { get; init; }
        public int ImportOffset { get; init; }
        public int DependsOffset { get; init; }
        public List<string> Names { get; init; } = [];
        public List<ImportInfo> Imports { get; init; } = [];
    }
    private readonly record struct ImportInfo(
        string ClassPackage,
        string ClassName,
        int OuterIndex,
        string ObjectName,
        int ObjectNumber);
    private readonly record struct FNameValue(
        int Index,
        int Number,
        string Name
    );
    private static string GetFallbackIconPath(int id)
    {
        return $"TslGame/Content/UI/HUD/EmoteWheel/Icons/emote_icon_{id}_dance";
    }
}