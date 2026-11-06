using DevHub.Core.Models;
using LibGit2Sharp;

namespace DevHub.Core.Services;

public sealed class LibGit2GitService : IGitService
{
    static LibGit2GitService() => GitEnvironment.Configure();

    public Task<GitStatusSummary?> GetStatusSummaryAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run<GitStatusSummary?>(() =>
        {
            if (!Repository.IsValid(path))
            {
                return null;
            }

            using var repository = new Repository(path);
            var head = repository.Head;
            var status = repository.RetrieveStatus(new StatusOptions
            {
                IncludeIgnored = false,
                RecurseIgnoredDirs = false,
                DetectRenamesInIndex = false,
                DetectRenamesInWorkDir = false,
            });

            return new GitStatusSummary(
                repository.Info.IsHeadDetached ? head.Tip?.Sha[..7] ?? "HEAD" : head.FriendlyName,
                head.TrackingDetails?.AheadBy ?? 0,
                head.TrackingDetails?.BehindBy ?? 0,
                status.Count(e => e.State is not (FileStatus.Ignored or FileStatus.Unaltered)));
        }, cancellationToken);
    }

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

            var workingChanges = new List<GitWorkingChange>();
            foreach (var entry in status)
            {
                if (entry.State == FileStatus.Ignored || entry.State == FileStatus.Unaltered)
                {
                    continue;
                }

                var (symbol, label) = entry.State switch
                {
                    var s when (s & (FileStatus.NewInIndex | FileStatus.NewInWorkdir)) != 0 =>
                        (s.HasFlag(FileStatus.NewInWorkdir) && !s.HasFlag(FileStatus.NewInIndex) ? "?" : "A",
                         s.HasFlag(FileStatus.NewInWorkdir) && !s.HasFlag(FileStatus.NewInIndex) ? "Untracked" : "Added"),
                    var s when (s & (FileStatus.DeletedFromIndex | FileStatus.DeletedFromWorkdir)) != 0 =>
                        ("D", "Deleted"),
                    var s when (s & (FileStatus.RenamedInIndex | FileStatus.RenamedInWorkdir)) != 0 =>
                        ("R", "Renamed"),
                    _ => ("M", "Modified"),
                };
                workingChanges.Add(new GitWorkingChange(entry.FilePath, symbol, label));
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
                WorkingChanges = workingChanges
                    .OrderBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StashCount = repository.Stashes.Count(),
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

    public Task<string> GetFileDiffAsync(
        string path, string relativeFilePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repository = new Repository(path);
            var patch = repository.Diff.Compare<Patch>(
                repository.Head.Tip?.Tree,
                DiffTargets.Index | DiffTargets.WorkingDirectory,
                [relativeFilePath]);

            if (!string.IsNullOrWhiteSpace(patch.Content) && patch.Content.Contains("@@", StringComparison.Ordinal))
            {
                return patch.Content;
            }

            // Untracked file: show its lines prefixed with '+'
            var fullPath = Path.Combine(path, relativeFilePath);
            if (File.Exists(fullPath))
            {
                var lines = File.ReadLines(fullPath).Take(300).Select(l => $"+{l}").ToList();
                return $"--- /dev/null\n+++ b/{relativeFilePath}\n@@ -0,0 +1,{lines.Count} @@\n" + string.Join("\n", lines);
            }

            return "";
        }, cancellationToken);
    }

    public Task CommitAllAsync(string path, string message, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Commit message cannot be empty.", nameof(message));
            }

            using var repository = new Repository(path);
            Commands.Stage(repository, "*");
            var now = DateTimeOffset.Now;
            var signature = repository.Config.BuildSignature(now)
                ?? new Signature(Environment.UserName, $"{Environment.UserName}@localhost", now);
            repository.Commit(message.Trim(), signature, signature);
        }, cancellationToken);
    }

    public Task<string> StashAsync(string path, CancellationToken cancellationToken = default) =>
        RunGitCliAsync(path, "stash push -u", cancellationToken);

    public Task<string> StashPopAsync(string path, CancellationToken cancellationToken = default) =>
        RunGitCliAsync(path, "stash pop", cancellationToken);

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
