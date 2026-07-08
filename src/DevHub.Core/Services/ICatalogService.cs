using DevHub.Core.Models;

namespace DevHub.Core.Services;

public interface ICatalogService
{
    /// <summary>The curated tool catalog, in display order (grouped by category).</summary>
    IReadOnlyList<CatalogTool> GetTools();
}
