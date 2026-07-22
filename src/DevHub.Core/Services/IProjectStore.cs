using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>Local cache of scanned projects so the list appears instantly on launch.</summary>
public interface IProjectStore
{
    Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the cached snapshot with the latest scan result.</summary>
    Task ReplaceAllAsync(IReadOnlyList<Project> projects, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates a single project (matched by Path) without touching the rest of the table.</summary>
    Task UpsertAsync(Project project, CancellationToken cancellationToken = default);
}
