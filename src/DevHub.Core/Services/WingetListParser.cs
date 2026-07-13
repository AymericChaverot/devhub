namespace DevHub.Core.Services;

/// <summary>One row of a `winget list` table. Available is the upgrade version, when winget shows one.</summary>
public sealed record WingetListEntry(string Name, string Id, string Version, string? Available = null);

/// <summary>
/// Parses the fixed-width table that `winget list` prints.
/// Locale-agnostic: column positions are derived from the header token offsets
/// (the column *order* — Name, Id, Version, ... — is the same in every language).
/// </summary>
public static class WingetListParser
{
    /// <returns>winget package id → installed version.</returns>
    public static IReadOnlyDictionary<string, string> Parse(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ParseEntries(output))
        {
            result[entry.Id] = entry.Version;
        }

        return result;
    }

    /// <summary>
    /// Full rows (Name, Id, Version). For packages winget could not map to a catalog
    /// id (installed outside winget), Id is a synthetic ARP/MSIX registry key rather
    /// than a real winget id — <see cref="IsSyntheticId"/> flags those.
    /// </summary>
    public static IReadOnlyList<WingetListEntry> ParseEntries(string output)
    {
        var entries = new List<WingetListEntry>();
        var lines = output
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(StripProgressArtifacts)
            .ToList();

        // The header is the line right above the dashed separator.
        var separatorIndex = lines.FindIndex(l => l.Length > 10 && l.All(c => c == '-'));
        if (separatorIndex < 1)
        {
            return entries;
        }

        var header = lines[separatorIndex - 1];
        var columnOffsets = GetColumnOffsets(header);
        if (columnOffsets.Count < 3)
        {
            return entries;
        }

        // Column order is fixed: 0=Name, 1=Id, 2=Version. A 5-column table also has
        // 3=Available, 4=Source; a 4-column one ends with Source and has no Available.
        int nameStart = columnOffsets[0], nameEnd = columnOffsets[1];
        int idStart = columnOffsets[1], idEnd = columnOffsets[2];
        int versionStart = columnOffsets[2];
        int versionEnd = columnOffsets.Count > 3 ? columnOffsets[3] : int.MaxValue;
        var hasAvailableColumn = columnOffsets.Count > 4;

        foreach (var line in lines.Skip(separatorIndex + 1))
        {
            if (line.Length <= versionStart)
            {
                continue;
            }

            var name = Slice(line, nameStart, nameEnd).Trim();
            var id = Slice(line, idStart, idEnd).Trim();
            var version = Slice(line, versionStart, versionEnd).Trim();

            string? available = null;
            if (hasAvailableColumn && line.Length > columnOffsets[3])
            {
                var value = Slice(line, columnOffsets[3], columnOffsets[4]).Trim();
                available = value.Length > 0 ? value : null;
            }

            if (id.Length > 0 && version.Length > 0 && !id.Contains('…'))
            {
                entries.Add(new WingetListEntry(name, id, version, available));
            }
        }

        return entries;
    }

    /// <summary>
    /// True for winget's fallback identifiers (e.g. "ARP\Machine\X64\{guid}") used when
    /// a package was installed outside winget and it could not confidently resolve the
    /// real catalog id in bulk-listing mode (an exact `--id` query still can).
    /// </summary>
    public static bool IsSyntheticId(string id) =>
        id.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase)
        || id.StartsWith("MSSTORE\\", StringComparison.OrdinalIgnoreCase);

    private static List<int> GetColumnOffsets(string header)
    {
        var offsets = new List<int>();
        var inToken = false;
        for (var i = 0; i < header.Length; i++)
        {
            if (!char.IsWhiteSpace(header[i]) && !inToken)
            {
                offsets.Add(i);
                inToken = true;
            }
            else if (char.IsWhiteSpace(header[i]))
            {
                inToken = false;
            }
        }

        return offsets;
    }

    private static string Slice(string line, int start, int end) =>
        line[start..Math.Min(end, line.Length)];

    /// <summary>Removes the spinner/backspace control characters winget emits before the table.</summary>
    private static string StripProgressArtifacts(string line)
    {
        var lastControl = line.LastIndexOfAny(['\b', '\r']);
        return lastControl >= 0 ? line[(lastControl + 1)..] : line;
    }
}
