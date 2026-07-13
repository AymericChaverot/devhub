using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevHub.Core.Services;

/// <summary>One regex match: position, matched text, captured groups.</summary>
public sealed record RegexMatchInfo(int Index, string Value, IReadOnlyList<string> Groups);

/// <summary>One line of a text diff. Marker is ' ' (common), '-' (left only) or '+' (right only).</summary>
public sealed record DiffLine(char Marker, string Text);

/// <summary>A parsed color in every representation the Toolbox shows.</summary>
public sealed record ColorInfo(byte R, byte G, byte B, string Hex, string Rgb, string Hsl);

/// <summary>Text representations the Toolbox converter understands.</summary>
public enum ToolTextFormat
{
    Text,
    Base64,
    Hex,
    Binary,
    Url,
}

/// <summary>
/// Pure text utilities behind the Toolbox page: format conversions, hashes,
/// JWT decoding, JSON formatting, timestamps. Invalid input throws
/// <see cref="FormatException"/> with a user-readable message.
/// </summary>
public static class DevToolbox
{
    /// <summary>Re-encodes <paramref name="input"/> from one representation to another (UTF-8 underneath).</summary>
    public static string Convert(string input, ToolTextFormat from, ToolTextFormat to)
    {
        return Encode(Decode(input, from), to);
    }

    private static string Decode(string input, ToolTextFormat format)
    {
        switch (format)
        {
            case ToolTextFormat.Text:
                return input;
            case ToolTextFormat.Url:
                return Uri.UnescapeDataString(input);
            case ToolTextFormat.Base64:
                try
                {
                    return Encoding.UTF8.GetString(System.Convert.FromBase64String(input.Trim()));
                }
                catch (FormatException)
                {
                    throw new FormatException("This is not valid Base64.");
                }

            case ToolTextFormat.Hex:
            {
                var clean = new string([.. input.Where(Uri.IsHexDigit)]);
                if (clean.Length == 0 || clean.Length % 2 != 0)
                {
                    throw new FormatException("Hex input needs an even number of hex digits.");
                }

                var bytes = new byte[clean.Length / 2];
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = System.Convert.ToByte(clean.Substring(i * 2, 2), 16);
                }

                return Encoding.UTF8.GetString(bytes);
            }

            case ToolTextFormat.Binary:
            {
                var clean = new string([.. input.Where(c => c is '0' or '1')]);
                if (clean.Length == 0 || clean.Length % 8 != 0)
                {
                    throw new FormatException("Binary input needs a multiple of 8 bits (0s and 1s).");
                }

                var bytes = new byte[clean.Length / 8];
                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = System.Convert.ToByte(clean.Substring(i * 8, 8), 2);
                }

                return Encoding.UTF8.GetString(bytes);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static string Encode(string text, ToolTextFormat format) => format switch
    {
        ToolTextFormat.Text => text,
        ToolTextFormat.Url => Uri.EscapeDataString(text),
        ToolTextFormat.Base64 => System.Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
        ToolTextFormat.Hex => string.Join(" ", Encoding.UTF8.GetBytes(text).Select(b => b.ToString("X2"))),
        ToolTextFormat.Binary => string.Join(" ", Encoding.UTF8.GetBytes(text).Select(b => System.Convert.ToString(b, 2).PadLeft(8, '0'))),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Hex digests of the classic algorithms, keyed by display name.</summary>
    public static IReadOnlyList<(string Algorithm, string Digest)> ComputeHashes(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return
        [
            ("MD5", System.Convert.ToHexStringLower(MD5.HashData(bytes))),
            ("SHA-1", System.Convert.ToHexStringLower(SHA1.HashData(bytes))),
            ("SHA-256", System.Convert.ToHexStringLower(SHA256.HashData(bytes))),
            ("SHA-512", System.Convert.ToHexStringLower(SHA512.HashData(bytes))),
        ];
    }

    /// <summary>Decodes a JWT without verifying its signature (header JSON, payload JSON).</summary>
    public static (string Header, string Payload) DecodeJwt(string token)
    {
        var parts = token.Trim().Split('.');
        if (parts.Length is not (2 or 3))
        {
            throw new FormatException("A JWT has 3 dot-separated parts (header.payload.signature).");
        }

        return (FormatJson(DecodeBase64Url(parts[0]), indent: true),
                FormatJson(DecodeBase64Url(parts[1]), indent: true));
    }

    private static string DecodeBase64Url(string part)
    {
        var s = part.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        try
        {
            return Encoding.UTF8.GetString(System.Convert.FromBase64String(s));
        }
        catch (FormatException)
        {
            throw new FormatException("This does not look like a valid JWT segment.");
        }
    }

    /// <summary>Pretty-prints (or minifies) JSON; throws <see cref="FormatException"/> with the parse error.</summary>
    public static string FormatJson(string json, bool indent)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = indent });
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Invalid JSON: {ex.Message}");
        }
    }

    /// <summary>Runs a regex over the input with a 1 s timeout (catastrophic backtracking guard).</summary>
    public static IReadOnlyList<RegexMatchInfo> TestRegex(
        string pattern, string input, bool ignoreCase, bool multiline)
    {
        Regex regex;
        try
        {
            var options = RegexOptions.None;
            if (ignoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            if (multiline)
            {
                options |= RegexOptions.Multiline;
            }

            regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Invalid pattern: {ex.Message}");
        }

        try
        {
            return [.. regex.Matches(input).Select(m => new RegexMatchInfo(
                m.Index,
                m.Value,
                [.. m.Groups.Cast<Group>().Skip(1).Select(g => $"{g.Name}={g.Value}")]))];
        }
        catch (RegexMatchTimeoutException)
        {
            throw new FormatException("The pattern timed out after 1 s — likely catastrophic backtracking.");
        }
    }

    /// <summary>Line-based diff (classic LCS). Marker: ' ' common, '-' left only, '+' right only.</summary>
    public static IReadOnlyList<DiffLine> DiffLines(string left, string right)
    {
        var a = left.Replace("\r\n", "\n").Split('\n');
        var b = right.Replace("\r\n", "\n").Split('\n');
        if (a.Length > 2000 || b.Length > 2000)
        {
            throw new FormatException("The diff supports up to 2000 lines per side.");
        }

        // Longest-common-subsequence table, then a backwards walk emits the lines.
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add(new DiffLine(' ', a[x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine('-', a[x]));
                x++;
            }
            else
            {
                result.Add(new DiffLine('+', b[y]));
                y++;
            }
        }

        while (x < a.Length)
        {
            result.Add(new DiffLine('-', a[x++]));
        }

        while (y < b.Length)
        {
            result.Add(new DiffLine('+', b[y++]));
        }

        return result;
    }

    /// <summary>Parses "#RGB", "#RRGGBB", "rgb(r, g, b)", "r,g,b" or "hsl(h, s%, l%)".</summary>
    public static ColorInfo ParseColor(string input)
    {
        var text = input.Trim().ToLowerInvariant();
        byte r, g, b;

        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 3)
            {
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            }

            if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
            {
                throw new FormatException("Hex colors are #RGB or #RRGGBB.");
            }

            r = System.Convert.ToByte(hex[..2], 16);
            g = System.Convert.ToByte(hex[2..4], 16);
            b = System.Convert.ToByte(hex[4..6], 16);
        }
        else if (text.StartsWith("hsl"))
        {
            var numbers = ExtractNumbers(text, 3, "hsl(h, s%, l%) needs three numbers.");
            (r, g, b) = HslToRgb(numbers[0], numbers[1] / 100, numbers[2] / 100);
        }
        else
        {
            var numbers = ExtractNumbers(text, 3, "RGB colors need three numbers, e.g. rgb(51, 102, 153).");
            if (numbers.Any(n => n is < 0 or > 255))
            {
                throw new FormatException("RGB channels go from 0 to 255.");
            }

            (r, g, b) = ((byte)numbers[0], (byte)numbers[1], (byte)numbers[2]);
        }

        var (h, s, l) = RgbToHsl(r, g, b);
        return new ColorInfo(
            r, g, b,
            $"#{r:X2}{g:X2}{b:X2}",
            $"rgb({r}, {g}, {b})",
            $"hsl({h:0}, {s * 100:0}%, {l * 100:0}%)");
    }

    private static double[] ExtractNumbers(string text, int count, string error)
    {
        var matches = Regex.Matches(text, @"-?\d+(\.\d+)?");
        if (matches.Count != count)
        {
            throw new FormatException(error);
        }

        return [.. matches.Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))];
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r1, g1, b1) = ((int)(h / 60)) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    private static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
    {
        double rd = r / 255d, gd = g / 255d, bd = b / 255d;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == rd
            ? ((gd - bd) / d + (gd < bd ? 6 : 0)) * 60
            : max == gd
                ? ((bd - rd) / d + 2) * 60
                : ((rd - gd) / d + 4) * 60;
        return (h, s, l);
    }

    /// <summary>Cryptographically random password with at least one char from each selected set.</summary>
    public static string GeneratePassword(int length, bool upper, bool lower, bool digits, bool symbols)
    {
        var sets = new List<string>();
        if (upper)
        {
            sets.Add("ABCDEFGHJKLMNPQRSTUVWXYZ");
        }

        if (lower)
        {
            sets.Add("abcdefghijkmnopqrstuvwxyz");
        }

        if (digits)
        {
            sets.Add("23456789");
        }

        if (symbols)
        {
            sets.Add("!@#$%^&*()-_=+[]{};:,.?/");
        }

        if (sets.Count == 0)
        {
            throw new FormatException("Pick at least one character set.");
        }

        if (length < sets.Count || length > 256)
        {
            throw new FormatException($"Length must be between {Math.Max(sets.Count, 4)} and 256.");
        }

        var pool = string.Concat(sets);
        var chars = new char[length];

        // One guaranteed char per selected set, the rest from the whole pool…
        for (var i = 0; i < length; i++)
        {
            var source = i < sets.Count ? sets[i] : pool;
            chars[i] = source[RandomNumberGenerator.GetInt32(source.Length)];
        }

        // …then a Fisher–Yates shuffle so the guaranteed chars aren't at the front.
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    /// <summary>Both directions of the unix-timestamp conversion, as display lines.</summary>
    public static string DescribeUnixTimestamp(long value)
    {
        // Values that large are almost certainly milliseconds.
        var isMilliseconds = Math.Abs(value) >= 100_000_000_000;
        var moment = isMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : DateTimeOffset.FromUnixTimeSeconds(value);

        var unit = isMilliseconds ? "milliseconds" : "seconds";
        return $"Interpreted as {unit}\nUTC:   {moment.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC\nLocal: {moment.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}";
    }
}
