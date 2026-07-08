namespace DevHub.Core.Services;

public interface IFileOperations
{
    /// <summary>Sends a folder to the Recycle Bin (never a permanent delete).</summary>
    Task RecycleAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Renames a folder in place and returns its new full path.</summary>
    Task<string> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken = default);
}
