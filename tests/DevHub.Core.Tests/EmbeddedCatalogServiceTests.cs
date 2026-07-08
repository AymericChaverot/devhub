using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class EmbeddedCatalogServiceTests
{
    [Fact]
    public void GetTools_LoadsTheCuratedCatalog()
    {
        var tools = new EmbeddedCatalogService().GetTools();

        Assert.True(tools.Count >= 30, $"expected a rich catalog, got {tools.Count} tools");
    }

    [Fact]
    public void EveryTool_HasAtLeastOneVariant_AndRequiredFields()
    {
        var tools = new EmbeddedCatalogService().GetTools();

        Assert.All(tools, tool =>
        {
            Assert.NotEmpty(tool.DisplayName);
            Assert.NotEmpty(tool.Category);
            Assert.NotEmpty(tool.Variants);
        });
    }

    [Fact]
    public void WingetIds_AreUniqueAcrossTheWholeCatalog()
    {
        var ids = new EmbeddedCatalogService().GetTools()
            .SelectMany(t => t.Variants)
            .Select(v => v.WingetId)
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AccentColors_WhenPresent_AreValidHexTriplets()
    {
        var tools = new EmbeddedCatalogService().GetTools();

        Assert.All(tools.Where(t => t.AccentColor is not null), tool =>
            Assert.Matches("^#[0-9A-Fa-f]{6}$", tool.AccentColor!));
    }
}
