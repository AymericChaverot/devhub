using System.Diagnostics;
using DevHub.Core.Models;

namespace DevHub.Core.Services;

/// <summary>Detects IDEs from well-known install locations (VS Code, Visual Studio, JetBrains, ...).</summary>
public sealed class InstalledIdeService : IIdeService
{
    private readonly Lazy<Task<IReadOnlyList<IdeInfo>>> _detected;

    public InstalledIdeService()
    {
        _detected = new(() => Task.Run(Detect));
    }

    public Task<IReadOnlyList<IdeInfo>> GetInstalledIdesAsync(CancellationToken cancellationToken = default) =>
        _detected.Value;

    public Task OpenAsync(IdeInfo ide, string projectPath)
    {
        Process.Start(new ProcessStartInfo(ide.ExecutablePath, $"\"{projectPath}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = projectPath,
        });
        return Task.CompletedTask;
    }

    private static IReadOnlyList<IdeInfo> Detect()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var ides = new List<IdeInfo>();

        void Probe(string id, string displayName, params string[] candidates)
        {
            var path = candidates.FirstOrDefault(File.Exists);
            if (path is not null)
            {
                ides.Add(new IdeInfo { Id = id, DisplayName = displayName, ExecutablePath = path });
            }
        }

        Probe("vscode", "Visual Studio Code",
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"));

        Probe("cursor", "Cursor",
            Path.Combine(localAppData, "Programs", "cursor", "Cursor.exe"));

        Probe("zed", "Zed",
            Path.Combine(localAppData, "Programs", "Zed", "zed.exe"),
            Path.Combine(localAppData, "Zed", "zed.exe"),
            Path.Combine(programFiles, "Zed", "zed.exe"));

        Probe("sublime", "Sublime Text",
            Path.Combine(programFiles, "Sublime Text", "sublime_text.exe"));

        // Visual Studio locates itself through vswhere.
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            var devenv = RunAndReadLine(vswhere, "-latest -products * -property productPath");
            if (devenv is not null && File.Exists(devenv))
            {
                ides.Add(new IdeInfo { Id = "visualstudio", DisplayName = "Visual Studio", ExecutablePath = devenv });
            }
        }

        // JetBrains Toolbox installs IDEs under %LOCALAPPDATA%\Programs\<Product>\bin\<exe>.
        var jetBrainsExes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["idea64.exe"] = "IntelliJ IDEA",
            ["rider64.exe"] = "Rider",
            ["webstorm64.exe"] = "WebStorm",
            ["pycharm64.exe"] = "PyCharm",
            ["clion64.exe"] = "CLion",
            ["goland64.exe"] = "GoLand",
            ["phpstorm64.exe"] = "PhpStorm",
            ["rustrover64.exe"] = "RustRover",
        };
        var programsDir = Path.Combine(localAppData, "Programs");
        if (Directory.Exists(programsDir))
        {
            foreach (var productDir in Directory.EnumerateDirectories(programsDir))
            {
                var bin = Path.Combine(productDir, "bin");
                if (!Directory.Exists(bin))
                {
                    continue;
                }

                foreach (var (exe, displayName) in jetBrainsExes)
                {
                    var path = Path.Combine(bin, exe);
                    if (File.Exists(path) && !ides.Any(i => i.Id == exe))
                    {
                        ides.Add(new IdeInfo
                        {
                            Id = exe.Replace("64.exe", ""),
                            DisplayName = displayName,
                            ExecutablePath = path,
                        });
                    }
                }
            }
        }

        return ides;
    }

    private static string? RunAndReadLine(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            var line = process?.StandardOutput.ReadLine();
            process?.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }
}
