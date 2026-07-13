using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class DevToolboxTests
{
    [Theory]
    [InlineData("Hello", ToolTextFormat.Base64, "SGVsbG8=")]
    [InlineData("Hello", ToolTextFormat.Hex, "48 65 6C 6C 6F")]
    [InlineData("Hi", ToolTextFormat.Binary, "01001000 01101001")]
    [InlineData("a b&c", ToolTextFormat.Url, "a%20b%26c")]
    public void Convert_FromText_EncodesEveryFormat(string text, ToolTextFormat to, string expected)
    {
        Assert.Equal(expected, DevToolbox.Convert(text, ToolTextFormat.Text, to));
    }

    [Theory]
    [InlineData("SGVsbG8=", ToolTextFormat.Base64, "Hello")]
    [InlineData("48 65 6c 6c 6f", ToolTextFormat.Hex, "Hello")]
    [InlineData("01001000 01101001", ToolTextFormat.Binary, "Hi")]
    [InlineData("a%20b%26c", ToolTextFormat.Url, "a b&c")]
    public void Convert_ToText_DecodesEveryFormat(string input, ToolTextFormat from, string expected)
    {
        Assert.Equal(expected, DevToolbox.Convert(input, from, ToolTextFormat.Text));
    }

    [Fact]
    public void Convert_BetweenNonTextFormats_RoundTrips()
    {
        // base64 → binary → base64
        var binary = DevToolbox.Convert("SGVsbG8=", ToolTextFormat.Base64, ToolTextFormat.Binary);
        Assert.Equal("SGVsbG8=", DevToolbox.Convert(binary, ToolTextFormat.Binary, ToolTextFormat.Base64));
    }

    [Theory]
    [InlineData("not base64!!", ToolTextFormat.Base64)]
    [InlineData("ABC", ToolTextFormat.Hex)]
    [InlineData("0101", ToolTextFormat.Binary)]
    public void Convert_InvalidInput_ThrowsFormatException(string input, ToolTextFormat from)
    {
        Assert.Throws<FormatException>(() => DevToolbox.Convert(input, from, ToolTextFormat.Text));
    }

    [Fact]
    public void ComputeHashes_KnownVector_MatchesSha256()
    {
        var hashes = DevToolbox.ComputeHashes("abc").ToDictionary(h => h.Algorithm, h => h.Digest);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hashes["SHA-256"]);
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", hashes["MD5"]);
        Assert.Equal(4, hashes.Count);
    }

    [Fact]
    public void DecodeJwt_SampleToken_ExposesHeaderAndPayload()
    {
        // {"alg":"HS256","typ":"JWT"} . {"sub":"1234567890","name":"John Doe"}
        const string token =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9"
            + ".eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIn0"
            + ".SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        var (header, payload) = DevToolbox.DecodeJwt(token);

        Assert.Contains("\"alg\": \"HS256\"", header);
        Assert.Contains("\"name\": \"John Doe\"", payload);
    }

    [Fact]
    public void DecodeJwt_Garbage_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => DevToolbox.DecodeJwt("definitely-not-a-jwt"));
    }

    [Fact]
    public void FormatJson_PrettyAndMinify_Work()
    {
        Assert.Contains("\n", DevToolbox.FormatJson("{\"a\":[1,2]}", indent: true));
        Assert.Equal("{\"a\":[1,2]}", DevToolbox.FormatJson("{ \"a\" : [ 1, 2 ] }", indent: false));
        Assert.Throws<FormatException>(() => DevToolbox.FormatJson("{oops", indent: true));
    }

    [Fact]
    public void TestRegex_FindsMatchesAndGroups()
    {
        var matches = DevToolbox.TestRegex(@"(\w+)@(\w+)\.com", "a@b.com then c@d.com", ignoreCase: false, multiline: false);

        Assert.Equal(2, matches.Count);
        Assert.Equal("a@b.com", matches[0].Value);
        Assert.Equal(0, matches[0].Index);
        Assert.Equal(["1=a", "2=b"], matches[0].Groups);
    }

    [Fact]
    public void TestRegex_IgnoreCaseOption_Works()
    {
        Assert.Empty(DevToolbox.TestRegex("abc", "ABC", ignoreCase: false, multiline: false));
        Assert.Single(DevToolbox.TestRegex("abc", "ABC", ignoreCase: true, multiline: false));
    }

    [Fact]
    public void TestRegex_InvalidPattern_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => DevToolbox.TestRegex("(unclosed", "x", false, false));
    }

    [Fact]
    public void DiffLines_MarksAddedRemovedAndCommonLines()
    {
        var diff = DevToolbox.DiffLines("a\nb\nc", "a\nx\nc");

        Assert.Equal([' ', '-', '+', ' '], diff.Select(d => d.Marker));
        Assert.Equal("b", diff[1].Text);
        Assert.Equal("x", diff[2].Text);
    }

    [Theory]
    [InlineData("#336699")]
    [InlineData("rgb(51, 102, 153)")]
    [InlineData("51, 102, 153")]
    public void ParseColor_AllRgbNotations_AgreeOnHex(string input)
    {
        Assert.Equal("#336699", DevToolbox.ParseColor(input).Hex);
    }

    [Fact]
    public void ParseColor_ShortHexAndHsl_Work()
    {
        Assert.Equal("#FFFFFF", DevToolbox.ParseColor("#fff").Hex);
        // hsl(210, 50%, 40%) ≈ #336699
        Assert.Equal("#336699", DevToolbox.ParseColor("hsl(210, 50%, 40%)").Hex);
        Assert.Throws<FormatException>(() => DevToolbox.ParseColor("bleu"));
    }

    [Fact]
    public void GeneratePassword_RespectsLengthAndSelectedSets()
    {
        var password = DevToolbox.GeneratePassword(24, upper: true, lower: true, digits: true, symbols: true);

        Assert.Equal(24, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));

        var digitsOnly = DevToolbox.GeneratePassword(12, upper: false, lower: false, digits: true, symbols: false);
        Assert.All(digitsOnly, c => Assert.True(char.IsDigit(c)));

        Assert.Throws<FormatException>(() => DevToolbox.GeneratePassword(12, false, false, false, false));
    }

    [Fact]
    public void DescribeUnixTimestamp_DetectsSecondsAndMilliseconds()
    {
        Assert.Contains("Interpreted as seconds", DevToolbox.DescribeUnixTimestamp(1_752_000_000));
        Assert.Contains("Interpreted as milliseconds", DevToolbox.DescribeUnixTimestamp(1_752_000_000_000));
        Assert.Contains("2025", DevToolbox.DescribeUnixTimestamp(1_752_000_000));
    }
}
