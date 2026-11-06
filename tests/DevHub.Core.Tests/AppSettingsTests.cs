using DevHub.Core.Models;

namespace DevHub.Core.Tests;

public sealed class AppSettingsTests
{
    private const string OldPath = @"C:\Projects\old";
    private const string NewPath = @"C:\Projects\new";

    [Fact]
    public void MoveProjectPath_RekeysEveryPerProjectSetting()
    {
        var openedAt = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings
        {
            ProjectIdePreferences = { [OldPath] = "vscode" },
            ProjectSubprojects = { [OldPath] = ["api", "web"] },
            ProjectLastOpened = { [OldPath] = openedAt },
            ProjectRunConfigs = { [OldPath] = [new RunConfig { Name = "dev", Command = "npm run dev" }] },
            ProjectTerminalShells = { [OldPath] = "pwsh" },
            PinnedProjectPaths = [@"C:\Projects\other", OldPath],
        };

        Assert.True(settings.MoveProjectPath(OldPath, NewPath));

        Assert.Equal("vscode", settings.ProjectIdePreferences[NewPath]);
        Assert.Equal(["api", "web"], settings.ProjectSubprojects[NewPath]);
        Assert.Equal(openedAt, settings.ProjectLastOpened[NewPath]);
        Assert.Equal("npm run dev", Assert.Single(settings.ProjectRunConfigs[NewPath]).Command);
        Assert.Equal("pwsh", settings.ProjectTerminalShells[NewPath]);
        Assert.Equal([@"C:\Projects\other", NewPath], settings.PinnedProjectPaths);
        Assert.False(settings.ProjectIdePreferences.ContainsKey(OldPath));
        Assert.False(settings.ProjectRunConfigs.ContainsKey(OldPath));
    }

    [Fact]
    public void MoveProjectPath_MatchesPathsCaseInsensitively()
    {
        var settings = new AppSettings
        {
            ProjectTerminalShells = { [OldPath.ToUpperInvariant()] = "cmd" },
            PinnedProjectPaths = [OldPath.ToUpperInvariant()],
        };

        Assert.True(settings.MoveProjectPath(OldPath, NewPath));
        Assert.Equal("cmd", settings.ProjectTerminalShells[NewPath]);
        Assert.Equal([NewPath], settings.PinnedProjectPaths);
    }

    [Fact]
    public void MoveProjectPath_UnknownProject_ReportsNoChange()
    {
        var settings = new AppSettings { ProjectIdePreferences = { [@"C:\Projects\other"] = "rider" } };

        Assert.False(settings.MoveProjectPath(OldPath, NewPath));
        Assert.Single(settings.ProjectIdePreferences);
    }
}
