using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class ProjectDiskAnalyzerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("devhub-disk-").FullName;

    [Fact]
    public void Analyze_FindsCleanableDirectoriesAndComputesSizes()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "README.md"), new byte[1000]);

        var nodeModules = Directory.CreateDirectory(Path.Combine(_tempDir, "node_modules", "pkg")).FullName;
        File.WriteAllBytes(Path.Combine(nodeModules, "index.js"), new byte[4096]);

        var binDir = Directory.CreateDirectory(Path.Combine(_tempDir, "src", "App", "bin")).FullName;
        File.WriteAllBytes(Path.Combine(binDir, "app.dll"), new byte[2048]);

        var report = ProjectDiskAnalyzer.Analyze(_tempDir);

        Assert.Equal(1000 + 4096 + 2048, report.TotalSizeBytes);
        Assert.Equal(4096 + 2048, report.CleanableSizeBytes);
        Assert.Equal(2, report.CleanableFolders.Count);
        Assert.Equal("node_modules", report.CleanableFolders[0].RelativePath);
        Assert.Equal(4096, report.CleanableFolders[0].SizeBytes);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5 * 1024 * 1024, "5.0 MB")]
    public void FormatBytes_FormatsExpectedUnits(long bytes, string expected)
    {
        Assert.Equal(expected, ProjectDiskAnalyzer.FormatBytes(bytes));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
