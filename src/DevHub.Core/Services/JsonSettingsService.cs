using System.Text.Json;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="filePath">Override for tests; defaults to %LOCALAPPDATA%\DevHub\settings.json.</param>
    public JsonSettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevHub", "settings.json");
    }

    public AppSettings Settings { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                Settings = new AppSettings();
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            Settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                       ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupt settings file must never prevent the app from starting.
            Settings = new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, Settings, SerializerOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
