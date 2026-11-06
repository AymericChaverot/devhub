using DevHub.Core.Models;
using DevHub.Core.Services;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private List<RunConfig> RunConfigs =>
        _project is not null && _settings.Settings.ProjectRunConfigs.TryGetValue(_project.Path, out var configs)
            ? configs
            : [];

    /// <summary>Rebuilds the Run split-button flyout from the project's saved and auto-detected configurations.</summary>
    private void RebuildRunMenu()
    {
        RunFlyout.Items.Clear();

        var configs = RunConfigs;
        var suggested = _project is not null
            ? RunConfigDetector.Detect(_project.Path)
                .Where(s => !configs.Any(c => string.Equals(c.Command, s.Command, StringComparison.OrdinalIgnoreCase)))
                .ToList()
            : [];

        var runLabel = Loc.T("Detail_Run", "Run");
        if (configs.Count > 0)
        {
            RunButtonLabel.Text = $"{runLabel} · {configs[0].Name}";
        }
        else if (suggested.Count > 0)
        {
            RunButtonLabel.Text = $"{runLabel} · {suggested[0].Name}";
        }
        else
        {
            RunButtonLabel.Text = runLabel;
        }

        foreach (var config in configs)
        {
            var item = new MenuFlyoutItem
            {
                Text = config.Name,
                Icon = new FontIcon { Glyph = "\uE768" },
                Tag = config,
            };
            ToolTipService.SetToolTip(item, config.Command);
            item.Click += (s, _) => RunConfiguration((RunConfig)((MenuFlyoutItem)s).Tag);
            RunFlyout.Items.Add(item);
        }

        if (configs.Count > 0)
        {
            RunFlyout.Items.Add(new MenuFlyoutSeparator());
        }

        if (suggested.Count > 0)
        {
            var suggestedMenu = new MenuFlyoutSubItem
            {
                Text = Loc.T("Detail_SuggestedConfigs", "Detected commands"),
                Icon = new FontIcon { Glyph = "\uE945" },
            };
            foreach (var sug in suggested)
            {
                var item = new MenuFlyoutItem
                {
                    Text = $"{sug.Name}  ({sug.Command})",
                    Icon = new FontIcon { Glyph = "\uE768" },
                    Tag = sug,
                };
                item.Click += async (s, _) => await SaveAndRunSuggestedConfigAsync((RunConfig)((MenuFlyoutItem)s).Tag);
                suggestedMenu.Items.Add(item);
            }

            RunFlyout.Items.Add(suggestedMenu);
        }

        var add = new MenuFlyoutItem
        {
            Text = Loc.T("Detail_AddConfig", "Add configuration…"),
            Icon = new FontIcon { Glyph = "\uE710" },
        };
        add.Click += async (_, _) => await AddRunConfigAsync();
        RunFlyout.Items.Add(add);

        if (configs.Count > 0)
        {
            var remove = new MenuFlyoutSubItem
            {
                Text = Loc.T("Detail_RemoveConfig", "Remove configuration"),
                Icon = new FontIcon { Glyph = "\uE74D" },
            };
            foreach (var config in configs)
            {
                var item = new MenuFlyoutItem { Text = config.Name, Tag = config };
                item.Click += async (s, _) => await RemoveRunConfigAsync((RunConfig)((MenuFlyoutItem)s).Tag);
                remove.Items.Add(item);
            }

            RunFlyout.Items.Add(remove);
        }
    }

    private async Task SaveAndRunSuggestedConfigAsync(RunConfig suggested)
    {
        if (_project is null)
        {
            return;
        }

        if (!_settings.Settings.ProjectRunConfigs.TryGetValue(_project.Path, out var configs))
        {
            configs = [];
            _settings.Settings.ProjectRunConfigs[_project.Path] = configs;
        }

        if (!configs.Any(c => string.Equals(c.Command, suggested.Command, StringComparison.OrdinalIgnoreCase)))
        {
            configs.Add(suggested);
            await _settings.SaveAsync();
            RebuildRunMenu();
        }

        RunConfiguration(suggested);
    }

    /// <summary>Main part of the split button: runs the default (first) configuration or first detected command.</summary>
    private async void Run_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        var configs = RunConfigs;
        if (configs.Count > 0)
        {
            RunConfiguration(configs[0]);
            return;
        }

        if (_project is not null)
        {
            var suggested = RunConfigDetector.Detect(_project.Path);
            if (suggested.Count > 0)
            {
                await SaveAndRunSuggestedConfigAsync(suggested[0]);
                return;
            }
        }

        await AddRunConfigAsync(runAfterAdd: true);
    }

    private void RunConfiguration(RunConfig config)
    {
        if (_project is null)
        {
            return;
        }

        Log.Information("Running {Config} for {Project}: {Command}", config.Name, _project.Name, config.Command);
        try
        {
            _terminal.OpenTerminal(_project.Path, config.Command, GetActiveTerminalShell());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open a terminal for {Project}", _project.Name);
        }
    }

    private async Task AddRunConfigAsync(bool runAfterAdd = false)
    {
        if (_project is null)
        {
            return;
        }

        var nameBox = new TextBox
        {
            Header = Loc.T("Detail_ConfigName", "Name"),
            PlaceholderText = "dev",
        };
        var commandBox = new TextBox
        {
            Header = Loc.T("Detail_ConfigCommand", "Command (runs in PowerShell, inside the project folder)"),
            PlaceholderText = "npm run dev",
            AcceptsReturn = false,
        };
        var dialog = new ContentDialog
        {
            Title = Loc.T("Detail_AddConfigTitle", "Add a run configuration"),
            PrimaryButtonText = Loc.T("Common_Add", "Add"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Spacing = 12, Children = { nameBox, commandBox } },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || commandBox.Text.Trim().Length == 0)
        {
            return;
        }

        var config = new RunConfig
        {
            Name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : "default",
            Command = commandBox.Text.Trim(),
        };

        if (!_settings.Settings.ProjectRunConfigs.TryGetValue(_project.Path, out var configs))
        {
            configs = [];
            _settings.Settings.ProjectRunConfigs[_project.Path] = configs;
        }

        configs.Add(config);
        await _settings.SaveAsync();
        RebuildRunMenu();

        if (runAfterAdd)
        {
            RunConfiguration(config);
        }
    }

    private async Task RemoveRunConfigAsync(RunConfig config)
    {
        if (_project is null)
        {
            return;
        }

        if (_settings.Settings.ProjectRunConfigs.TryGetValue(_project.Path, out var configs))
        {
            configs.Remove(config);
            if (configs.Count == 0)
            {
                _settings.Settings.ProjectRunConfigs.Remove(_project.Path);
            }

            await _settings.SaveAsync();
        }

        RebuildRunMenu();
    }
}
