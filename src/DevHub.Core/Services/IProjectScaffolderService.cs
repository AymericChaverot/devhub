using DevHub.Core.Models;

namespace DevHub.Core.Services;

public interface IProjectScaffolderService
{
    /// <summary>Whether <see cref="ProjectTemplate.CheckExecutable"/> runs on this machine.</summary>
    Task<bool> IsRuntimeAvailableAsync(ProjectTemplate template, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the project directory, scaffolds it from the chosen template, optionally wires up
    /// git and a remote, registers the result in DevHub's project store, and launches the
    /// preferred IDE. Reports one <see cref="ScaffoldProgress"/> per step transition (and, for the
    /// scaffold command itself, one per captured output line).
    /// </summary>
    Task<ScaffoldResult> ScaffoldAsync(
        ScaffoldRequest request, IProgress<ScaffoldProgress> progress, CancellationToken cancellationToken = default);
}
