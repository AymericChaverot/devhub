using DevHub.App.ViewModels;
using DevHub.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DevHub.App.Views;

public sealed partial class NewProjectWizardPage : Page
{
    public NewProjectWizardPage()
    {
        ViewModel = App.Services.GetRequiredService<NewProjectWizardViewModel>();
        InitializeComponent();
        ViewModel.ScaffoldCompleted += OnScaffoldCompleted;
        Loaded += OnLoaded;
    }

    public NewProjectWizardViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RemoteNoneRadio.IsChecked = true;
        NoStackRadio.IsChecked = true;
        await ViewModel.LoadAsync();
    }

    private void OnScaffoldCompleted(object? sender, EventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.Path = folder.Path;
        }
    }

    private void NoStackRadio_Checked(object sender, RoutedEventArgs e) => ViewModel.SelectedTemplate = null;

    private void TemplateRadio_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProjectTemplate template)
        {
            ViewModel.SelectedTemplate = template;
        }
    }

    private void RemoteModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag && Enum.TryParse<RemoteMode>(tag, out var mode))
        {
            ViewModel.RemoteMode = mode;
        }
    }

    private void InstallViaEnvironments_Click(object sender, RoutedEventArgs e) =>
        ShellPage.Current?.NavigateTo("environments");

    private void ErrorInfoBar_CloseButtonClick(InfoBar sender, object args) => ViewModel.ClearError();
}
