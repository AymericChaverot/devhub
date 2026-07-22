using DevHub.Core.Models;
using LibGit2Sharp;

namespace DevHub.Core.Services;

public sealed class LibGit2GitService : IGitService
{
    public Task<GitRepositoryInfo?> GetRepositoryInfoAsync(
        string path, int historyCount = 20, CancellationToken cancellationToken = default)
    {
        return Task.Run<GitRepositoryInfo?>(() =>
        {
            if (!Repository.IsValid(path))
            {
                return null;
            }

            using var repository = new Repository(path);
            var head = repository.Head;
            var status = repository.RetrieveStatus(new StatusOptions { IncludeIgnored = false });

            var commits = new List<GitCommitInfo>();
            var totalCommits = 0;
            if (head.Tip is not null)
            {
                commits.AddRange(repository.Commits
                    .Take(historyCount)
                    .Select(c => new GitCommitInfo(c.Sha[..7], c.MessageShort, c.Author.Name, c.Author.When)));

                var countFilter = new CommitFilter
                {
                    IncludeReachableFrom = repository.Branches
                        .Where(b => !b.IsRemote)
                        .Select(b => b.Tip)
                        .Where(t => t is not null)
                        .Cast<object>()
                        .Append(head.Tip)
                        .ToList(),
                };
                totalCommits = repository.Commits.QueryBy(countFilter).Count();
            }

            return new GitRepositoryInfo
            {
                CurrentBranch = repository.Info.IsHeadDetached
                    ? $"(detached at {head.Tip?.Sha[..7]})"
                    : head.FriendlyName,
                IsDetached = repository.Info.IsHeadDetached,
                Ahead = head.TrackingDetails?.AheadBy ?? 0,
                Behind = head.TrackingDetails?.BehindBy ?? 0,
                Modified = status.Modified.Count() + status.Staged.Count(),
                Added = status.Added.Count(),
                Removed = status.Removed.Count() + status.Missing.Count(),
                Untracked = status.Untracked.Count(),
                Branches = repository.Branches
                    // origin/HEAD is a pointer, not a real branch.
                    .Where(b => !b.FriendlyName.EndsWith("/HEAD", StringComparison.Ordinal))
                    .Select(b => new GitBranchInfo(b.FriendlyName, b.IsCurrentRepositoryHead, b.IsRemote))
                    .OrderByDescending(b => b.IsCurrent)
                    .ThenBy(b => b.IsRemote)
                    .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Commits = commits,
                TotalCommits = totalCommits,
            };
        }, cancellationToken);
    }

    public Task CheckoutAsync(string path, string branchName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repository = new Repository(path);
            var branch = repository.Branches[branchName]
                ?? throw new ArgumentException($"Branch '{branchName}' does not exist.");

            // Remote branch: check out a local tracking branch instead, like IDEs do.
            if (branch.IsRemote)
            {
                var localName = branchName[(branchName.IndexOf('/') + 1)..];
                var local = repository.Branches[localName];
                if (local is null)
                {
                    local = repository.CreateBranch(localName, branch.Tip);
                    repository.Branches.Update(local, b => b.TrackedBranch = branch.CanonicalName);
                }

                branch = local;
            }

            Commands.Checkout(repository, branch);
        }, cancellationToken);
    }

    public Task<GitGraph?> GetCommitGraphAsync(
        string path, int maxCount = 150, CancellationToken cancellationToken = default)
    {
        return Task.Run<GitGraph?>(() =>
        {
            if (!Repository.IsValid(path))
            {
                return null;
            }

            using var repository = new Repository(path);
            var tips = repository.Branches
                .Where(b => !b.IsRemote)
                .Select(b => b.Tip)
                .Where(t => t is not null)
                .Cast<object>()
                .ToList();
            if (repository.Head.Tip is not null)
            {
                tips.Add(repository.Head.Tip);
            }

            if (tips.Count == 0)
            {
                return new GitGraph { Rows = [], MaxLane = 0 };
            }

            var refsBySha = new Dictionary<string, List<string>>();
            void AddRef(string sha, string name)
            {
                if (!refsBySha.TryGetValue(sha, out var list))
                {
                    refsBySha[sha] = list = [];
                }

                if (!list.Contains(name))
                {
                    list.Add(name);
                }
            }

            foreach (var branch in repository.Branches.Where(b => !b.IsRemote && b.Tip is not null))
            {
                AddRef(branch.Tip.Sha, branch.FriendlyName);
            }

            foreach (var tag in repository.Tags)
            {
                AddRef(tag.Target.Sha, $"🏷 {tag.FriendlyName}");
            }

            var filter = new CommitFilter
            {
                IncludeReachableFrom = tips,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            };

            var commits = repository.Commits.QueryBy(filter)
                .Take(maxCount)
                .Select(c => new GitGraphCommit(
                    c.Sha,
                    c.MessageShort,
                    c.Author.Name,
                    c.Author.When,
                    c.Parents.Select(p => p.Sha).ToList(),
                    refsBySha.GetValueOrDefault(c.Sha, [])))
                .ToList();

            return GitGraphBuilder.Build(commits);
        }, cancellationToken);
    }

    public Task CreateBranchAsync(string path, string branchName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repository = new Repository(path);
            var branch = repository.CreateBranch(branchName.Trim());
            Commands.Checkout(repository, branch);
        }, cancellationToken);
    }

    public Task<string> FetchAsync(string path, CancellationToken cancellationToken = default) =>
        RunGitCliAsync(path, "fetch --all --prune", cancellationToken);

    public Task<string> PullAsync(string path, CancellationToken cancellationToken = default) =>
        RunGitCliAsync(path, "pull", cancellationToken);

    public Task<string> PushAsync(string path, CancellationToken cancellationToken = default) =>
        RunGitCliAsync(path, "push", cancellationToken);

    public Task<string> PushAsync(
        string path, string remoteName, string branchName, bool setUpstream, CancellationToken cancellationToken = default) =>
        RunGitCliAsync(path, $"push {(setUpstream ? "-u " : "")}{remoteName} {branchName}", cancellationToken);

    public async Task<bool> IsGitCliAvailableAsync()
    {
        try
        {
            await RunGitCliAsync(Environment.CurrentDirectory, "--version", CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Network ops go through git.exe so Git Credential Manager handles auth.</summary>
    private static async Task<string> RunGitCliAsync(
        string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        // git writes progress/summary to stderr even on success.
        var output = ((await stdout) + "\n" + (await stderr)).Trim();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(output.Length > 0 ? output : $"git exited with code {process.ExitCode}");
        }

        return output;
    }

    public Task<string> CloneAsync(string url, string parentDirectory, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var folderName = DeriveFolderName(url);
            var target = Path.Combine(parentDirectory, folderName);
            if (Directory.Exists(target))
            {
                throw new InvalidOperationException($"The folder '{folderName}' already exists.");
            }

            Repository.Clone(url, target);
            return target;
        }, cancellationToken);
    }

    /// <summary>"https://github.com/user/my-repo.git" → "my-repo". Also handles local paths.</summary>
    internal static string DeriveFolderName(string url)
    {
        var trimmed = url.TrimEnd('/', '\\');
        var lastSegment = trimmed[(trimmed.LastIndexOfAny(['/', '\\', ':']) + 1)..];
        var name = lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? lastSegment[..^4]
            : lastSegment;

        if (name.Length == 0)
        {
            throw new ArgumentException($"Cannot derive a folder name from '{url}'.", nameof(url));
        }

        return name;
    }
}
