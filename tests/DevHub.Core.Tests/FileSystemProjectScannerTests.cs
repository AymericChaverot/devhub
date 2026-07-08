using DevHub.Core.Services;
using LibGit2Sharp;

namespace DevHub.Core.Tests;

public sealed class FileSystemProjectScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("devhub-scan-").FullName;

    [Fact]
    public async Task Scan_MissingFolder_ReturnsEmpty()
    {
        var result = await new FileSystemProjectScanner().ScanAsync(Path.Combine(_root, "nope"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Scan_ListsFirstLevelFolders_SkippingHiddenAndDotFolders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "beta"));
        Directory.CreateDirectory(Path.Combine(_root, ".cache"));
        var hidden = Directory.CreateDirectory(Path.Combine(_root, "secret"));
        hidden.Attributes |= FileAttributes.Hidden;
        File.WriteAllText(Path.Combine(_root, "stray-file.txt"), "not a project");

        var result = await new FileSystemProjectScanner().ScanAsync(_root);

        Assert.Equal(["alpha", "beta"], result.Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Scan_DetectsGitRepositoryAndOriginRemote()
    {
        var repoPath = Directory.CreateDirectory(Path.Combine(_root, "my-repo")).FullName;
        Repository.Init(repoPath);
        using (var repo = new Repository(repoPath))
        {
            repo.Network.Remotes.Add("origin", "https://github.com/example/my-repo.git");
        }

        Directory.CreateDirectory(Path.Combine(_root, "plain-folder"));

        var result = await new FileSystemProjectScanner().ScanAsync(_root);

        var gitProject = Assert.Single(result, p => p.Name == "my-repo");
        Assert.True(gitProject.IsGitRepository);
        Assert.Equal("https://github.com/example/my-repo.git", gitProject.GitRemoteUrl);

        var plain = Assert.Single(result, p => p.Name == "plain-folder");
        Assert.False(plain.IsGitRepository);
        Assert.Null(plain.GitRemoteUrl);
    }

    public void Dispose()
    {
        // libgit2 marks some object files read-only; clear attributes before deleting.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }
}
