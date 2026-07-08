namespace DevHub.App.ViewModels;

/// <summary>One category section of the Environments page (used as a grouped CollectionViewSource source).</summary>
public sealed class PackageGroup : List<ToolCardViewModel>
{
    public PackageGroup(string category, IEnumerable<ToolCardViewModel> items)
        : base(items)
    {
        Category = category;
    }

    public string Category { get; }
}
