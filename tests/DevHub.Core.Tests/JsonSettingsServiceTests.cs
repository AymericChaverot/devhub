using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("devhub-tests-").FullName;

    private string SettingsPath => Path.Combine(_tempDir, "settings.json");

    [Fact]
    public async Task Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var service = new JsonSettingsService(SettingsPath);

        await service.LoadAsync();

        Assert.Null(service.Settings.ProjectsFolderPath);
        Assert.Equal("System", service.Settings.Theme);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAllValues()
    {
        var writer = new JsonSettingsService(SettingsPath);
        await writer.LoadAsync();
        writer.Settings.ProjectsFolderPath = @"C:\Users\aymeric\Projects";
        writer.Settings.PreferredIdeId = "vscode";
        writer.Settings.Theme = "Dark";
        await writer.SaveAsync();

        var reader = new JsonSettingsService(SettingsPath);
        await reader.LoadAsync();

        Assert.Equal(@"C:\Users\aymeric\Projects", reader.Settings.ProjectsFolderPath);
        Assert.Equal("vscode", reader.Settings.PreferredIdeId);
        Assert.Equal("Dark", reader.Settings.Theme);
    }

    [Fact]
    public async Task Load_WhenFileIsCorrupt_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(SettingsPath, "{ not valid json !!");
        var service = new JsonSettingsService(SettingsPath);

        await service.LoadAsync();

        Assert.Equal("System", service.Settings.Theme);
    }

    [Fact]
    public async Task Save_CreatesMissingDirectories()
    {
        var nested = Path.Combine(_tempDir, "a", "b", "settings.json");
        var service = new JsonSettingsService(nested);
        await service.LoadAsync();

        await service.SaveAsync();

        Assert.True(File.Exists(nested));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
