using DevHub.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace DevHub.App.Views;

public sealed partial class EnvVariablesPage : Page
{
    public EnvVariablesPage()
    {
        ViewModel = App.Services.GetRequiredService<EnvVariablesViewModel>();
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Load();
    }

    public EnvVariablesViewModel ViewModel { get; }

    private void ScopeBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.SelectedScopeIndex = sender.Items.IndexOf(sender.SelectedItem);

    private async void NewVariable_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = Loc.T("EnvVar_NameHeader", "Name"),
            PlaceholderText = "MY_VARIABLE",
        };
        var valueBox = new TextBox
        {
            Header = Loc.T("EnvVar_ValueHeader", "Value"),
            TextWrapping = TextWrapping.Wrap,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("EnvVar_NewTitle", "New variable"),
            Content = new StackPanel { Spacing = 12, MinWidth = 400, Children = { nameBox, valueBox } },
            PrimaryButtonText = Loc.T("Common_Save", "Save"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SaveVariableAsync(nameBox.Text, valueBox.Text);
        }
    }

    private async void DeleteVariable_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVariable is not { } variable)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.F("EnvVar_DeleteTitle", "Delete \"{0}\"?", variable.Name),
            Content = Loc.T("EnvVar_DeleteText", "The variable will be removed from the registry. Consider exporting a backup first."),
            PrimaryButtonText = Loc.T("EnvVar_DeleteAction", "Delete"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteVariableAsync(variable);
        }
    }

    private async void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        var text = await PromptForPathAsync(Loc.T("EnvVar_AddEntryTitle", "Add entry"), "");
        if (text is not null)
        {
            await ViewModel.AddEntryAsync(text);
        }
    }

    private async void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not EnvPathEntryViewModel entry)
        {
            return;
        }

        var text = await PromptForPathAsync(Loc.T("EnvVar_EditEntryTitle", "Edit entry"), entry.Path);
        if (text is not null)
        {
            await ViewModel.UpdateEntryAsync(entry, text);
        }
    }

    private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EnvPathEntryViewModel entry)
        {
            await ViewModel.DeleteEntryAsync(entry);
        }
    }

    private async void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EnvPathEntryViewModel entry)
        {
            await ViewModel.MoveEntryAsync(entry, -1);
        }
    }

    private async void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EnvPathEntryViewModel entry)
        {
            await ViewModel.MoveEntryAsync(entry, +1);
        }
    }

    private void CopyEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EnvPathEntryViewModel entry)
        {
            CopyToClipboard(entry.Path);
        }
    }

    private void OpenEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is EnvPathEntryViewModel entry)
        {
            OpenInExplorer(entry.ExpandedPath);
        }
    }

    private void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVariable is { } variable)
        {
            CopyToClipboard(variable.Value);
        }
    }

    private void OpenValue_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedVariable is { } variable)
        {
            OpenInExplorer(Environment.ExpandEnvironmentVariables(variable.Value));
        }
    }

    private void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        ViewModel.StatusMessage = Loc.T("EnvVar_Copied", "Copied to clipboard.");
    }

    private static void OpenInExplorer(string path) => ShellLauncher.OpenFolder(path);

    private async Task<string?> PromptForPathAsync(string title, string initialText)
    {
        var pathBox = new TextBox
        {
            Text = initialText,
            PlaceholderText = @"C:\tools\bin",
            MinWidth = 420,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = pathBox,
            PrimaryButtonText = Loc.T("Common_Save", "Save"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? pathBox.Text : null;
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"devhub-env-{DateTime.Now:yyyyMMdd-HHmm}",
        };
        picker.FileTypeChoices.Add("JSON", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await ViewModel.ExportAsync(file.Path);
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var backup = await ViewModel.ReadBackupAsync(file.Path);

            var userCheck = new CheckBox
            {
                Content = Loc.F("EnvVar_RestoreUser", "User variables ({0})", backup.User.Count),
                IsChecked = backup.User.Count > 0,
                IsEnabled = backup.User.Count > 0,
            };
            var systemCheck = new CheckBox
            {
                Content = Loc.F("EnvVar_RestoreSystem", "System variables ({0})", backup.System.Count),
                IsChecked = false,
                IsEnabled = backup.System.Count > 0,
            };

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Loc.T("EnvVar_RestoreTitle", "Restore from backup"),
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = Loc.F(
                                "EnvVar_RestoreText",
                                "Backup from {0}, exported {1:g}. Existing variables with the same name will be overwritten; others are kept.",
                                backup.MachineName, backup.ExportedAt.LocalDateTime),
                            TextWrapping = TextWrapping.Wrap,
                        },
                        userCheck,
                        systemCheck,
                    },
                },
                PrimaryButtonText = Loc.T("EnvVar_Restore", "Restore"),
                CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.RestoreAsync(backup, userCheck.IsChecked == true, systemCheck.IsChecked == true);
            }
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = Loc.F("EnvVar_ImportError", "Could not read the backup file: {0}", ex.Message);
        }
    }

    private void ErrorInfoBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearError();

    private void StatusInfoBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearStatus();
}
