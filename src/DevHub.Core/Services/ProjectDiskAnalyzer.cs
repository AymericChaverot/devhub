using System.Globalization;

namespace DevHub.Core.Services;

/// <summary>One cleanable artifact or dependency directory found inside a project.</summary>
public sealed record CleanableFolderInfo(
    string RelativePath,
    string FullPath,
    string Category,
    long SizeBytes)
{
    public string FormattedSize => ProjectDiskAnalyzer.FormatBytes(SizeBytes);
}

/// <summary>Summary of a project's disk usage and its cleanable artifact/dependency folders.</summary>
public sealed record ProjectDiskReport(
    long TotalSizeBytes,
    long CleanableSizeBytes,
    IReadOnlyList<CleanableFolderInfo> CleanableFolders)
{
    public string FormattedTotalSize => ProjectDiskAnalyzer.FormatBytes(TotalSizeBytes);

    public string FormattedCleanableSize => ProjectDiskAnalyzer.FormatBytes(CleanableSizeBytes);
}

/// <summary>
/// Scans a project folder to measure disk space and locate dependency/build-artifact
/// directories (node_modules, bin, obj, target, .venv, dist, etc.) that can be safely cleaned.
/// </summary>
public static class ProjectDiskAnalyzer
{
    private static readonly Dictionary<string, string> CleanableDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["node_modules"] = "Node.js (node_modules)",
            ["bin"] = ".NET / Build (bin)",
            ["obj"] = ".NET / Build (obj)",
            ["target"] = "Rust / Maven (target)",
            [".venv"] = "Python (.venv)",
            ["venv"] = "Python (venv)",
            ["__pycache__"] = "Python (__pycache__)",
            [".pytest_cache"] = "Python (.pytest_cache)",
            ["dist"] = "Build (dist)",
            [".next"] = "Next.js (.next)",
            [".nuxt"] = "Nuxt (.nuxt)",
            [".turbo"] = "Turborepo (.turbo)",
            [".parcel-cache"] = "Parcel (.parcel-cache)",
            [".gradle"] = "Gradle (.gradle)",
        };

    public static ProjectDiskReport Analyze(string projectPath, int maxSearchDepth = 3)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return new ProjectDiskReport(0, 0, []);
        }

        var cleanable = new List<CleanableFolderInfo>();
        var totalSize = ScanDirectoryRecursive(projectPath, projectPath, 0, maxSearchDepth, cleanable);

        var sortedCleanable = cleanable
            .Where(c => c.SizeBytes > 0)
            .OrderByDescending(c => c.SizeBytes)
            .ThenBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cleanableTotal = sortedCleanable.Sum(c => c.SizeBytes);
        return new ProjectDiskReport(totalSize, cleanableTotal, sortedCleanable);
    }

    private static long ScanDirectoryRecursive(
        string rootPath,
        string currentPath,
        int depth,
        int maxCleanableDepth,
        List<CleanableFolderInfo> cleanable)
    {
        long size = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(currentPath))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch
                {
                    // Skip locked/inaccessible files
                }
            }

            foreach (var subDir in Directory.EnumerateDirectories(currentPath))
            {
                var dirName = Path.GetFileName(subDir);
                if (string.IsNullOrEmpty(dirName))
                {
                    continue;
                }

                // Skip reparse points (symlinks/junctions) to avoid infinite loops or double-counting.
                try
                {
                    if ((File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (depth < maxCleanableDepth
                    && CleanableDirectoryNames.TryGetValue(dirName, out var category))
                {
                    var folderSize = ComputeDirectorySize(subDir);
                    size += folderSize;
                    if (folderSize > 0)
                    {
                        var relative = Path.GetRelativePath(rootPath, subDir);
                        cleanable.Add(new CleanableFolderInfo(relative, subDir, category, folderSize));
                    }
                }
                else
                {
                    size += ScanDirectoryRecursive(rootPath, subDir, depth + 1, maxCleanableDepth, cleanable);
                }
            }
        }
        catch
        {
            // Ignore permission errors
        }

        return size;
    }

    public static long ComputeDirectorySize(string directoryPath)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(directoryPath);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Ignore
                    }
                }

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    try
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0)
                        {
                            stack.Push(sub);
                        }
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        return total;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#} KB", kb);
        }

        var mb = kb / 1024.0;
        if (mb < 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", mb);
        }

        var gb = mb / 1024.0;
        return string.Format(CultureInfo.InvariantCulture, "{0:0.00} GB", gb);
    }
}
