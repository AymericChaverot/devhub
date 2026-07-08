using DevHub.Core.Models;
using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class SqliteProjectStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("devhub-db-").FullName;

    private string DbPath => Path.Combine(_dir, "test.db");

    [Fact]
    public async Task GetAll_OnFreshDatabase_ReturnsEmpty()
    {
        var store = new SqliteProjectStore(DbPath);

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task ReplaceAll_ThenGetAll_RoundTripsProjects()
    {
        var store = new SqliteProjectStore(DbPath);
        var scannedAt = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);
        await store.ReplaceAllAsync(
        [
            new Project { Name = "b-project", Path = @"C:\p\b-project", IsGitRepository = false, LastScannedAt = scannedAt },
            new Project { Name = "a-project", Path = @"C:\p\a-project", GitRemoteUrl = "https://github.com/x/a.git", IsGitRepository = true, LastScannedAt = scannedAt },
        ]);

        var all = await store.GetAllAsync();

        Assert.Equal(["a-project", "b-project"], all.Select(p => p.Name).ToArray());
        var a = all[0];
        Assert.Equal(@"C:\p\a-project", a.Path);
        Assert.Equal("https://github.com/x/a.git", a.GitRemoteUrl);
        Assert.True(a.IsGitRepository);
        Assert.Equal(scannedAt, a.LastScannedAt);
    }

    [Fact]
    public async Task ReplaceAll_OverwritesThePreviousSnapshot()
    {
        var store = new SqliteProjectStore(DbPath);
        await store.ReplaceAllAsync(
            [new Project { Name = "old", Path = @"C:\p\old", IsGitRepository = false, LastScannedAt = DateTime.UtcNow }]);

        await store.ReplaceAllAsync(
            [new Project { Name = "new", Path = @"C:\p\new", IsGitRepository = false, LastScannedAt = DateTime.UtcNow }]);

        var all = await store.GetAllAsync();
        Assert.Equal("new", Assert.Single(all).Name);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }
}
