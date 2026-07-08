using DevHub.Core.Models;

namespace DevHub.App.ViewModels;

/// <summary>
/// Navigation payload for the project detail page. Subprojects carry a relative
/// display path so the hero shows "Backend" rather than the full absolute path.
/// </summary>
public sealed record ProjectDetailArgs(Project Project, string? RelativePathDisplay = null);
