using DevHub.Core.Models;

namespace DevHub.Core.Services;

public interface IProjectScanner
{
    /// <summary>
    /// Scans the Projects folder: each first-level subfolder is one project.
    /// Hidden and dot-prefixed folders are skipped.
    /// </summary>
    Task<IReadOnlyList<Project>> ScanAsync(string projectsFolder, CancellationToken cancellationToken = default);
}
