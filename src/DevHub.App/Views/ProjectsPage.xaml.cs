using DevHub.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DevHub.App.Views;

public sealed partial class ProjectsPage : Page
{
    public ProjectsPage()
    {
        ViewModel = App.Services.GetRequiredService<ProjectsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ProjectsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private void Project_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ProjectViewModel project)
        {
            Frame.Navigate(typeof(ProjectDetailPage), project.Project);
        }
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await ViewModel.SetProjectsFolderAsync(folder.Path);
        }
    }

    private void NewProject_Click(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(NewProjectWizardPage));

    private async void AddFromGit_Click(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://github.com/user/repo.git",
            MinWidth = 400,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.T("Proj_AddFromGitTitle", "Add a project from Git"),
            Content = urlBox,
            PrimaryButtonText = Loc.T("Proj_Clone", "Clone"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.AddFromGitAsync(urlBox.Text);
        }
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProjectViewModel project)
        {
            ShellLauncher.OpenFolder(project.Path);
        }
    }

    private async void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProjectViewModel project)
        {
            await ViewModel.TogglePinAsync(project);
        }
    }

    private async void RemoveFromList_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProjectViewModel project)
        {
            await ViewModel.RemoveFromListAsync(project);
        }
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProjectViewModel project)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Loc.F("Proj_DeleteTitle", "Delete \"{0}\"?", project.Name),
            Content = Loc.T("Proj_DeleteText", "The folder will be moved to the Recycle Bin, so you can restore it from there if needed."),
            PrimaryButtonText = Loc.T("Proj_MoveToBin", "Move to Recycle Bin"),
            CloseButtonText = Loc.T("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteToRecycleBinAsync(project);
        }
    }

    private void ErrorInfoBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearError();
}
