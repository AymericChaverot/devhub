using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class RegFileWriterTests
{
    private const string KeyPath = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    [Fact]
    public void Build_PlainValue_WritesQuotedStringWithEscapes()
    {
        var script = RegFileWriter.Build(KeyPath, new Dictionary<string, string>
        {
            ["MY_DIR"] = @"C:\Program Files\My ""App""",
        });

        Assert.StartsWith("Windows Registry Editor Version 5.00", script);
        Assert.Contains($"[{KeyPath}]", script);
        Assert.Contains(@"""MY_DIR""=""C:\\Program Files\\My \""App\""""", script);
    }

    [Fact]
    public void Build_ValueWithPercent_WritesExpandSzHex()
    {
        var script = RegFileWriter.Build(KeyPath, new Dictionary<string, string> { ["X"] = "%A%" });

        // "%A%" as UTF-16LE with the null terminator: 25 00 41 00 25 00 00 00.
        Assert.Contains("\"X\"=hex(2):25,00,41,00,25,00,00,00", script);
    }

    [Fact]
    public void Build_Deletes_WriteRemovalMarkers()
    {
        var script = RegFileWriter.Build(KeyPath, new Dictionary<string, string>(), ["OLD_VAR"]);

        Assert.Contains("\"OLD_VAR\"=-", script);
    }
}
