using System.Text.RegularExpressions;

namespace SaveStateConverter;

/// <summary>Game titles by disc serial, from PCSX2's game database (catalog/game-titles.tsv).</summary>
public static class GameTitles
{
    private const string FileName = "game-titles.tsv";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Titles = new(Load);

    /// <summary>Title for a serial such as "SLUS-20013", or null when unknown or the list is unavailable.</summary>
    public static string? Find(string? serial) =>
        serial is not null && Titles.Value.TryGetValue(serial, out var title) ? title : null;

    /// <summary>Number of known titles (0 when the list is unavailable).</summary>
    public static int Count => Titles.Value.Count;

    private static IReadOnlyDictionary<string, string> Load()
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(ShippedData.ReadText(FileName) ?? "");
        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (line.StartsWith('#')) continue;
            var tab = line.IndexOf('\t');
            if (tab > 0 && tab < line.Length - 1)
                titles[line[..tab]] = line[(tab + 1)..];
        }
        return titles;
    }
}

/// <summary>The parts of a PCSX2 save-state file name: "SERIAL (CRC).slot.p2s", optionally ".backup".</summary>
public sealed partial record SaveStateName(string Serial, string Crc, string Slot, bool IsBackup)
{
    public static SaveStateName? Parse(string fileName)
    {
        var match = Pattern().Match(Path.GetFileName(fileName));
        return match.Success
            ? new SaveStateName(match.Groups["serial"].Value, match.Groups["crc"].Value.ToUpperInvariant(),
                match.Groups["slot"].Value, match.Groups["backup"].Success)
            : null;
    }

    /// <summary>"Slot 1", "Resume", "Slot 10 (PCSX2 backup)".</summary>
    public string SlotText
    {
        get
        {
            var slot = Slot.Equals("resume", StringComparison.OrdinalIgnoreCase)
                ? "Resume"
                : int.TryParse(Slot, out var number) ? $"Slot {number}" : $"Slot {Slot}";
            return IsBackup ? $"{slot} (PCSX2 backup)" : slot;
        }
    }

    [GeneratedRegex(@"^(?<serial>.+?) \((?<crc>[0-9A-Fa-f]{8})\)\.(?<slot>\d{1,2}|resume)\.p2s(?<backup>\.backup)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
