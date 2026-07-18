namespace DevHub.App.ViewModels;

/// <summary>
/// One entry of a delimited variable (a PATH segment) with its warning badges.
/// The list is rebuilt after every mutation, so the entry itself is immutable.
/// </summary>
public sealed class EnvPathEntryViewModel(string path, bool isMissing, bool isDuplicate, int index, int count)
{
    public string Path { get; } = path;

    /// <summary>The entry with %VAR% references expanded; what Copy and Explorer act on.</summary>
    public string ExpandedPath { get; } = Environment.ExpandEnvironmentVariables(path);

    /// <summary>The entry looks like an absolute path but nothing exists there on disk.</summary>
    public bool IsMissing { get; } = isMissing;

    public bool IsDuplicate { get; } = isDuplicate;

    public bool IsOpenableFolder { get; } =
        System.IO.Directory.Exists(Environment.ExpandEnvironmentVariables(path));

    public int Index { get; } = index;

    public bool CanMoveUp { get; } = index > 0;

    public bool CanMoveDown { get; } = index < count - 1;
}
