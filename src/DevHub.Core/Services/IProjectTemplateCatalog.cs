using DevHub.Core.Models;

namespace DevHub.Core.Services;

public interface IProjectTemplateCatalog
{
    /// <summary>The predefined stacks the New Project wizard offers, in display order.</summary>
    IReadOnlyList<ProjectTemplate> GetTemplates();
}
