using LibGit2Sharp;

namespace DevHub.Core.Services;

/// <summary>Process-wide libgit2 configuration, applied once before the first repository is opened.</summary>
internal static class GitEnvironment
{
    private static readonly Lazy<bool> Configuration =
        new(Apply, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Applies the configuration on the first call; later calls are no-ops.</summary>
    public static void Configure() => _ = Configuration.Value;

    private static bool Apply()
    {
        try
        {
            // libgit2 refuses to open a repository whose folder is not owned by the current user
            // ("dubious ownership"). On Windows a .git folder becomes owned by the Administrators
            // group as soon as a single git command runs from an elevated shell, and every open
            // then throws — which DevHub could only read as "this is not a repository": no
            // history, no branches, no remote, no "View on GitHub" button.
            // Dropping the check is safe here: libgit2 is used for reading only (status, branches,
            // log) and runs no hooks, filters or pagers, while fetch/pull/push go through git.exe,
            // which applies its own ownership rules.
            GlobalSettings.SetOwnerValidation(false);
        }
        catch (Exception)
        {
            // Older libgit2 without the setting: repositories owned by someone else keep being
            // reported as plain folders, which is the previous behaviour.
        }

        return true;
    }
}
