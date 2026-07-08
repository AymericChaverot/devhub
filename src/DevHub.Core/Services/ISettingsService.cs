using DevHub.Core.Models;

namespace DevHub.Core.Services;

public interface ISettingsService
{
    /// <summary>The current settings. Call <see cref="LoadAsync"/> once at startup before reading.</summary>
    AppSettings Settings { get; }

    /// <summary>Loads settings from disk, or initializes defaults when no file exists yet.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the current settings to disk.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
