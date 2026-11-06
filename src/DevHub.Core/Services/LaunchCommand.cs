using System.Text;

namespace DevHub.Core.Services;

/// <summary>
/// What DevHub was asked to do when launched with arguments (from a taskbar jump list entry,
/// or a second launch redirected to the running instance).
/// </summary>
public abstract record LaunchCommand
{
    private const string ProjectSwitch = "--project";
    private const string PaletteSwitch = "--palette";

    private LaunchCommand()
    {
    }

    /// <summary>Command-line arguments that open the given project's page.</summary>
    public static string ProjectArguments(string projectPath) => $"{ProjectSwitch} \"{projectPath}\"";

    /// <summary>Command-line arguments that open the command palette.</summary>
    public static string PaletteArguments => PaletteSwitch;

    /// <summary>
    /// Reads a command line (with or without the executable in front). Null when it holds
    /// no DevHub command, which is a plain launch.
    /// </summary>
    public static LaunchCommand? Parse(string? commandLine)
    {
        var tokens = Tokenize(commandLine ?? "");
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Equals(PaletteSwitch, StringComparison.OrdinalIgnoreCase))
            {
                return new ShowPalette();
            }

            if (tokens[i].Equals(ProjectSwitch, StringComparison.OrdinalIgnoreCase)
                && i + 1 < tokens.Count
                && tokens[i + 1].Length > 0)
            {
                return new OpenProject(tokens[i + 1]);
            }
        }

        return null;
    }

    /// <summary>Splits on spaces, keeping "quoted parts" together (paths with spaces).</summary>
    private static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>Open the detail page of the project at <paramref name="Path"/>.</summary>
    public sealed record OpenProject(string Path) : LaunchCommand;

    /// <summary>Open the command palette.</summary>
    public sealed record ShowPalette : LaunchCommand;
}
