using DevHub.Core.Services;
using LibGit2Sharp;

namespace DevHub.Core.Tests;

public sealed class LibGit2GitServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("devhub-git-").FullName;

    [Theory]
    [InlineData("https://github.com/user/my-repo.git", "my-repo")]
    [InlineData("https://github.com/user/my-repo", "my-repo")]
    [InlineData("https://gitlab.com/group/sub/tool.git/", "tool")]
    [InlineData("git@github.com:user/ssh-repo.git", "ssh-repo")]
    public void DeriveFolderName_HandlesCommonUrlShapes(string url, string expected)
    {
        Assert.Equal(expected, LibGit2GitService.DeriveFolderName(url));
    }

    [Fact]
    public async Task Clone_FromLocalRepository_CreatesWorkingCopy()
    {
        // Arrange: a local source repository with one commit.
        var sourcePath = Directory.CreateDirectory(Path.Combine(_root, "source-repo")).FullName;
        Repository.Init(sourcePath);
        using (var repo = new Repository(sourcePath))
        {
            File.WriteAllText(Path.Combine(sourcePath, "README.md"), "# hello");
            Commands.Stage(repo, "README.md");
            var author = new Signature("Test", "test@example.com", DateTimeOffset.Now);
            repo.Commit("initial", author, author);
        }

        var targetParent = Directory.CreateDirectory(Path.Combine(_root, "projects")).FullName;

        var clonedPath = await new LibGit2GitService().CloneAsync(sourcePath, targetParent);

        Assert.Equal(Path.Combine(targetParent, "source-repo"), clonedPath);
        Assert.True(File.Exists(Path.Combine(clonedPath, "README.md")));
        Assert.True(Repository.IsValid(clonedPath));
    }

    [Fact]
    public async Task Clone_WhenTargetFolderExists_Throws()
    {
        var targetParent = Directory.CreateDirectory(Path.Combine(_root, "projects")).FullName;
        Directory.CreateDirectory(Path.Combine(targetParent, "taken"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LibGit2GitService().CloneAsync("https://example.com/x/taken.git", targetParent));
    }

    [Fact]
    public async Task GetRepositoryInfo_NonRepository_ReturnsNull()
    {
        var plain = Directory.CreateDirectory(Path.Combine(_root, "plain")).FullName;

        Assert.Null(await new LibGit2GitService().GetRepositoryInfoAsync(plain));
    }

    [Fact]
    public async Task GetRepositoryInfo_ReportsBranchCommitsAndDirtyState()
    {
        var repoPath = CreateRepositoryWithCommits("repo-info", "first commit", "second commit");
        File.WriteAllText(Path.Combine(repoPath, "untracked.txt"), "new");
        File.WriteAllText(Path.Combine(repoPath, "file0.txt"), "changed content");

        var info = await new LibGit2GitService().GetRepositoryInfoAsync(repoPath);

        Assert.NotNull(info);
        Assert.False(info.IsDetached);
        Assert.Equal(2, info.Commits.Count);
        Assert.Equal("second commit", info.Commits[0].Message);
        Assert.Equal(1, info.Untracked);
        Assert.Equal(1, info.Modified);
        Assert.True(info.IsDirty);
        var branch = Assert.Single(info.Branches);
        Assert.True(branch.IsCurrent);
        Assert.Equal(info.CurrentBranch, branch.Name);
    }

    [Fact]
    public async Task Checkout_SwitchesBranch()
    {
        var repoPath = CreateRepositoryWithCommits("repo-checkout", "initial");
        using (var repo = new Repository(repoPath))
        {
            repo.CreateBranch("feature/x");
        }

        var service = new LibGit2GitService();
        await service.CheckoutAsync(repoPath, "feature/x");

        var info = await service.GetRepositoryInfoAsync(repoPath);
        Assert.Equal("feature/x", info!.CurrentBranch);
        Assert.Equal(2, info.Branches.Count);
        Assert.True(info.Branches.Single(b => b.Name == "feature/x").IsCurrent);
    }

    [Fact]
    public async Task Checkout_UnknownBranch_Throws()
    {
        var repoPath = CreateRepositoryWithCommits("repo-nobranch", "initial");

        await Assert.ThrowsAsync<ArgumentException>(
            () => new LibGit2GitService().CheckoutAsync(repoPath, "does-not-exist"));
    }

    [Fact]
    public async Task GetCommitGraph_OnMergedRepository_ProducesMergeRow()
    {
        var repoPath = CreateRepositoryWithCommits("repo-graph", "initial");
        using (var repo = new Repository(repoPath))
        {
            var author = new Signature("Test", "test@example.com", DateTimeOffset.Now);
            var feature = repo.CreateBranch("feature");
            Commands.Checkout(repo, feature);
            File.WriteAllText(Path.Combine(repoPath, "feature.txt"), "x");
            Commands.Stage(repo, "feature.txt");
            repo.Commit("feature work", author, author);

            Commands.Checkout(repo, repo.Branches.Single(b => b.FriendlyName != "feature" && !b.IsRemote));
            File.WriteAllText(Path.Combine(repoPath, "main.txt"), "y");
            Commands.Stage(repo, "main.txt");
            repo.Commit("main work", author, author);

            repo.Merge(repo.Branches["feature"], author, new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward });
        }

        var graph = await new LibGit2GitService().GetCommitGraphAsync(repoPath);

        Assert.NotNull(graph);
        Assert.Equal(4, graph.Rows.Count);
        Assert.True(graph.MaxLane >= 1);
        var mergeRow = graph.Rows[0];
        Assert.Equal(2, mergeRow.BottomSegments.Count);
        Assert.Contains(graph.Rows, r => r.Refs.Contains("feature"));
    }

    [Fact]
    public async Task CreateBranch_CreatesAndChecksOut()
    {
        var repoPath = CreateRepositoryWithCommits("repo-newbranch", "initial");

        var service = new LibGit2GitService();
        await service.CreateBranchAsync(repoPath, "exp/idea");

        var info = await service.GetRepositoryInfoAsync(repoPath);
        Assert.Equal("exp/idea", info!.CurrentBranch);
    }

    [Fact]
    public async Task WorkingChangesDiffAndCommitAll_WorkEndToEnd()
    {
        var repoPath = CreateRepositoryWithCommits("repo-quickcommit", "initial");
        File.WriteAllText(Path.Combine(repoPath, "file0.txt"), "updated content\nline 2");
        File.WriteAllText(Path.Combine(repoPath, "added.txt"), "brand new file");

        var service = new LibGit2GitService();
        var infoBefore = await service.GetRepositoryInfoAsync(repoPath);
        Assert.NotNull(infoBefore);
        Assert.Equal(2, infoBefore.WorkingChanges.Count);
        Assert.Contains(infoBefore.WorkingChanges, c => c.RelativePath == "file0.txt" && c.StatusSymbol == "M");
        Assert.Contains(infoBefore.WorkingChanges, c => c.RelativePath == "added.txt" && c.StatusSymbol == "?");

        var modifiedDiff = await service.GetFileDiffAsync(repoPath, "file0.txt");
        Assert.Contains("+updated content", modifiedDiff);

        var untrackedDiff = await service.GetFileDiffAsync(repoPath, "added.txt");
        Assert.Contains("+brand new file", untrackedDiff);

        await service.CommitAllAsync(repoPath, "feat: quick commit");

        var infoAfter = await service.GetRepositoryInfoAsync(repoPath);
        Assert.NotNull(infoAfter);
        Assert.False(infoAfter.IsDirty);
        Assert.Empty(infoAfter.WorkingChanges);
        Assert.Equal("feat: quick commit", infoAfter.Commits[0].Message);
    }

    [Fact]
    public async Task GetStatusSummary_NonRepository_ReturnsNull()
    {
        var plain = Directory.CreateDirectory(Path.Combine(_root, "plain-summary")).FullName;

        Assert.Null(await new LibGit2GitService().GetStatusSummaryAsync(plain));
    }

    [Fact]
    public async Task GetStatusSummary_CountsChangedFiles()
    {
        var repoPath = CreateRepositoryWithCommits("repo-summary", "first commit");
        File.WriteAllText(Path.Combine(repoPath, "untracked.txt"), "new");
        File.WriteAllText(Path.Combine(repoPath, "file0.txt"), "changed content");

        var summary = await new LibGit2GitService().GetStatusSummaryAsync(repoPath);

        Assert.NotNull(summary);
        Assert.Equal(2, summary.ChangedFiles);
        Assert.True(summary.IsDirty);
        Assert.Equal(0, summary.Ahead);
        Assert.Equal(0, summary.Behind);
        Assert.False(string.IsNullOrEmpty(summary.Branch));
    }

    [Fact]
    public async Task GetStatusSummary_ReportsAheadAndBehindUpstream()
    {
        var originPath = CreateRepositoryWithCommits("origin-summary", "c1", "c2");
        var clonePath = Repository.Clone(originPath, Path.Combine(_root, "clone-summary"));
        var author = new Signature("Test", "test@example.com", DateTimeOffset.Now);

        // One new commit on each side: the clone is 1 ahead and, after fetching, 1 behind.
        using (var origin = new Repository(originPath))
        {
            File.WriteAllText(Path.Combine(originPath, "remote.txt"), "remote");
            Commands.Stage(origin, "remote.txt");
            origin.Commit("remote change", author, author);
        }

        using (var clone = new Repository(clonePath))
        {
            File.WriteAllText(Path.Combine(clone.Info.WorkingDirectory, "local.txt"), "local");
            Commands.Stage(clone, "local.txt");
            clone.Commit("local change", author, author);
            Commands.Fetch(clone, "origin", [], null, null);
        }

        var summary = await new LibGit2GitService().GetStatusSummaryAsync(clonePath);

        Assert.NotNull(summary);
        Assert.Equal(1, summary.Ahead);
        Assert.Equal(1, summary.Behind);
        Assert.False(summary.IsDirty);
    }

    private string CreateRepositoryWithCommits(string name, params string[] messages)
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        Repository.Init(path);
        using var repo = new Repository(path);
        var author = new Signature("Test", "test@example.com", DateTimeOffset.Now);
        for (var i = 0; i < messages.Length; i++)
        {
            File.WriteAllText(Path.Combine(path, $"file{i}.txt"), $"content {i}");
            Commands.Stage(repo, $"file{i}.txt");
            repo.Commit(messages[i], author, author);
        }

        return path;
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }
}
