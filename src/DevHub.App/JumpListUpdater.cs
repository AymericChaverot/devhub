using DevHub.Core.Services;
using Serilog;
using Windows.UI.StartScreen;

namespace DevHub.App;

/// <summary>
/// Keeps the taskbar jump list (right-click on DevHub's taskbar icon) in sync: the most
/// recently opened projects, plus a command palette task. Each entry relaunches DevHub with
/// <see cref="LaunchCommand"/> arguments, which the running instance picks up.
/// </summary>
public sealed class JumpListUpdater(ISettingsService settings, IProjectStore store)
{
    private const int MaxRecentProjects = 5;

    public async Task UpdateAsync()
    {
        try
        {
            // Jump lists need package identity: they are absent from unpackaged dev builds.
            if (!IsPackaged() || !JumpList.IsSupported())
            {
                return;
            }

            var jumpList = await JumpList.LoadCurrentAsync();
            jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
            jumpList.Items.Clear();

            var recentGroup = Loc.T("Jump_RecentGroup", "Recent projects");
            foreach (var (name, path) in await GetRecentProjectsAsync())
            {
                var item = JumpListItem.CreateWithArguments(LaunchCommand.ProjectArguments(path), name);
                item.Description = path;
                item.GroupName = recentGroup;
                jumpList.Items.Add(item);
            }

            // No group name: Windows lists it under "Tasks".
            jumpList.Items.Add(JumpListItem.CreateWithArguments(
                LaunchCommand.PaletteArguments,
                Loc.T("Jump_Palette", "Command palette")));

            await jumpList.SaveAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not update the taskbar jump list");
        }
    }

    private async Task<List<(string Name, string Path)>> GetRecentProjectsAsync()
    {
        var projects = (await store.GetAllAsync())
            .ToDictionary(p => p.Path, p => p.Name, StringComparer.OrdinalIgnoreCase);
        var ignored = settings.Settings.IgnoredProjectPaths;

        return settings.Settings.ProjectLastOpened
            .Where(e => projects.ContainsKey(e.Key) && !ignored.Contains(e.Key, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Value)
            .Take(MaxRecentProjects)
            .Select(e => (projects[e.Key], e.Key))
            .ToList();
    }

    private static bool IsPackaged()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
