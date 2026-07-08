using DevHub.Core.Services;

namespace DevHub.Core.Tests;

public sealed class ShellFileOperationsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("devhub-rename-").FullName;

    [Fact]
    public async Task RenameFolder_MovesTheFolderAndReturnsNewPath()
    {
        var original = Directory.CreateDirectory(Path.Combine(_root, "old-name")).FullName;
        File.WriteAllText(Path.Combine(original, "file.txt"), "content");

        var newPath = await new ShellFileOperations().RenameFolderAsync(original, "new-name");

        Assert.Equal(Path.Combine(_root, "new-name"), newPath);
        Assert.False(Directory.Exists(original));
        Assert.Equal("content", File.ReadAllText(Path.Combine(newPath, "file.txt")));
    }

    [Fact]
    public async Task RenameFolder_ToExistingName_Throws()
    {
        var original = Directory.CreateDirectory(Path.Combine(_root, "one")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "two"));

        await Assert.ThrowsAsync<IOException>(
            () => new ShellFileOperations().RenameFolderAsync(original, "two"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("in|valid")]
    public async Task RenameFolder_InvalidName_Throws(string invalidName)
    {
        var original = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;

        await Assert.ThrowsAsync<ArgumentException>(
            () => new ShellFileOperations().RenameFolderAsync(original, invalidName));
    }

    [Fact]
    public async Task RenameFolder_SameName_IsANoOp()
    {
        var original = Directory.CreateDirectory(Path.Combine(_root, "same")).FullName;

        var result = await new ShellFileOperations().RenameFolderAsync(original, "same");

        Assert.Equal(original, result);
        Assert.True(Directory.Exists(original));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
