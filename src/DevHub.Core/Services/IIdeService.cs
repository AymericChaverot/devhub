using DevHub.Core.Models;

namespace DevHub.Core.Services;

public interface IIdeService
{
    /// <summary>IDEs/editors found on this machine. Detected once, then cached.</summary>
    Task<IReadOnlyList<IdeInfo>> GetInstalledIdesAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a project folder in the given IDE.</summary>
    Task OpenAsync(IdeInfo ide, string projectPath);
}
