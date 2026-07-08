using DevHub.Core.Models;
using LibGit2Sharp;

namespace DevHub.Core.Services;

public sealed class FileSystemProjectScanner : IProjectScanner
{
    public Task<IReadOnlyList<Project>> ScanAsync(
        string projectsFolder, CancellationToken cancellationToken = default)
    {
        // Directory enumeration and libgit2 calls are blocking; keep them off the UI thread.
        return Task.Run<IReadOnlyList<Project>>(() =>
        {
            if (!Directory.Exists(projectsFolder))
            {
                return [];
            }

            var projects = new List<Project>();
            foreach (var directory in Directory.EnumerateDirectories(projectsFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var info = new DirectoryInfo(directory);
                if (info.Name.StartsWith('.') || info.Attributes.HasFlag(FileAttributes.Hidden))
                {
                    continue;
                }

                var (isGit, remoteUrl) = InspectGit(directory);
                projects.Add(new Project
                {
                    Name = info.Name,
                    Path = info.FullName,
                    IsGitRepository = isGit,
                    GitRemoteUrl = remoteUrl,
                    LastScannedAt = DateTime.UtcNow,
                    Languages = LanguageDetector.Detect(directory),
                    Frameworks = FrameworkDetector.Detect(directory),
                });
            }

            return projects;
        }, cancellationToken);
    }

    private static (bool IsGit, string? RemoteUrl) InspectGit(string directory)
    {
        try
        {
            if (!Repository.IsValid(directory))
            {
                return (false, null);
            }

            using var repository = new Repository(directory);
            return (true, repository.Network.Remotes["origin"]?.Url);
        }
        catch
        {
            // A broken .git folder must not break the whole scan.
            return (false, null);
        }
    }
}
