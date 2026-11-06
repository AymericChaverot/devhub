using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class LaunchCommandTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"C:\\Program Files\\DevHub\\DevHub.App.exe\"")]
    [InlineData("--project")]
    [InlineData("--unknown value")]
    public void Parse_WithoutCommand_ReturnsNull(string? commandLine)
    {
        Assert.Null(LaunchCommand.Parse(commandLine));
    }

    [Theory]
    [InlineData("--project \"C:\\My Projects\\devhub\"")]
    [InlineData("\"C:\\Apps\\DevHub.App.exe\" --project \"C:\\My Projects\\devhub\"")]
    [InlineData("C:\\Apps\\DevHub.App.exe   --PROJECT   \"C:\\My Projects\\devhub\"")]
    public void Parse_ProjectSwitch_ReturnsPath(string commandLine)
    {
        var command = Assert.IsType<LaunchCommand.OpenProject>(LaunchCommand.Parse(commandLine));
        Assert.Equal("C:\\My Projects\\devhub", command.Path);
    }

    [Theory]
    [InlineData("--palette")]
    [InlineData("\"C:\\Apps\\DevHub.App.exe\" --palette")]
    public void Parse_PaletteSwitch_ReturnsShowPalette(string commandLine)
    {
        Assert.IsType<LaunchCommand.ShowPalette>(LaunchCommand.Parse(commandLine));
    }

    [Fact]
    public void ProjectArguments_RoundTrip()
    {
        const string path = "C:\\Users\\me\\Documents\\Projects\\with space";

        var command = LaunchCommand.Parse(LaunchCommand.ProjectArguments(path));

        Assert.Equal(new LaunchCommand.OpenProject(path), command);
    }

    [Fact]
    public void PaletteArguments_RoundTrip()
    {
        Assert.IsType<LaunchCommand.ShowPalette>(LaunchCommand.Parse(LaunchCommand.PaletteArguments));
    }
}
