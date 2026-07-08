using System.Diagnostics;
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
            Title = "Add a project from Git",
            Content = urlBox,
            PrimaryButtonText = "Clone",
            CloseButtonText = "Cancel",
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
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{project.Path}\"") { UseShellExecute = true });
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
            Title = $"Delete \"{project.Name}\"?",
            Content = "The folder will be moved to the Recycle Bin, so you can restore it from there if needed.",
            PrimaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteToRecycleBinAsync(project);
        }
    }

    private void ErrorInfoBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearError();
}
