using System.Reflection;
using System.Text.Json;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>Reads the curated catalog embedded in this assembly (Resources/catalog.json).</summary>
public sealed class EmbeddedCatalogService : ICatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Lazy<IReadOnlyList<CatalogTool>> _tools = new(Load);

    public IReadOnlyList<CatalogTool> GetTools() => _tools.Value;

    private static IReadOnlyList<CatalogTool> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("DevHub.Core.Resources.catalog.json")
            ?? throw new InvalidOperationException("Embedded catalog.json not found.");
        return JsonSerializer.Deserialize<List<CatalogTool>>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("Embedded catalog.json is empty.");
    }
}
