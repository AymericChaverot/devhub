using System.Text;

namespace DevHub.Core.Services;

/// <summary>
/// Builds .reg (Registration Entries, version 5) scripts. Used to batch machine-scope
/// environment writes into a single elevated "reg import", so one UAC prompt covers
/// any number of set / delete operations.
/// </summary>
internal static class RegFileWriter
{
    /// <summary>
    /// One key section that sets <paramref name="sets"/> and deletes <paramref name="deletes"/>.
    /// Values containing '%' are written as REG_EXPAND_SZ (hex(2)), others as plain strings.
    /// </summary>
    public static string Build(string keyPath, IReadOnlyDictionary<string, string> sets, IEnumerable<string>? deletes = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.Append('[').Append(keyPath).AppendLine("]");

        foreach (var (name, value) in sets)
        {
            sb.Append('"').Append(EscapeName(name)).Append("\"=");
            sb.AppendLine(value.Contains('%') ? ToExpandSzHex(value) : $"\"{EscapeName(value)}\"");
        }

        foreach (var name in deletes ?? [])
        {
            sb.Append('"').Append(EscapeName(name)).AppendLine("\"=-");
        }

        return sb.ToString();
    }

    private static string EscapeName(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // .reg encodes REG_EXPAND_SZ as hex(2): little-endian UTF-16 bytes with a null terminator.
    private static string ToExpandSzHex(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value + '\0');
        return "hex(2):" + string.Join(',', bytes.Select(b => b.ToString("x2")));
    }
}
