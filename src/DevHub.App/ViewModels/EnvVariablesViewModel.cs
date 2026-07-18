using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

public sealed partial class EnvVariablesViewModel : ObservableObject
{
    private readonly IEnvironmentVariableService _service;

    private List<EnvVariableItemViewModel> _all = [];

    public EnvVariablesViewModel(IEnvironmentVariableService service)
    {
        _service = service;
        SearchText = "";
        EditValueText = "";
    }

    public ObservableCollection<EnvVariableItemViewModel> Variables { get; } = [];

    public ObservableCollection<EnvPathEntryViewModel> Entries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowElevationWarning))]
    public partial int SelectedScopeIndex { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    [NotifyPropertyChangedFor(nameof(ShowListEditor))]
    [NotifyPropertyChangedFor(nameof(ShowTextEditor))]
    [NotifyPropertyChangedFor(nameof(SelectedValueIsFolder))]
    public partial EnvVariableItemViewModel? SelectedVariable { get; set; }

    /// <summary>Raw value being edited in the single-value editor; applied by SaveValueAsync.</summary>
    [ObservableProperty]
    public partial string EditValueText { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    public bool HasStatus => StatusMessage is not null;

    public bool HasSelection => SelectedVariable is not null;

    public bool ShowPlaceholder => SelectedVariable is null;

    public bool ShowListEditor => SelectedVariable?.IsMultiValued == true;

    public bool ShowTextEditor => SelectedVariable is { IsMultiValued: false };

    /// <summary>The selected single value points at an existing folder (enables "Open in Explorer").</summary>
    public bool SelectedValueIsFolder =>
        SelectedVariable is { } variable
        && Directory.Exists(Environment.ExpandEnvironmentVariables(variable.Value));

    /// <summary>At least one entry carries a badge; enables the one-click repair.</summary>
    public bool HasEntryWarnings => Entries.Any(e => e.IsMissing || e.IsDuplicate);

    public EnvVariableScope CurrentScope => SelectedScopeIndex == 1 ? EnvVariableScope.Machine : EnvVariableScope.User;

    /// <summary>System tab + not running as admin: writes will show a UAC prompt.</summary>
    public bool ShowElevationWarning => SelectedScopeIndex == 1 && !_service.IsElevated;

    public void ClearError() => ErrorMessage = null;

    public void ClearStatus() => StatusMessage = null;

    partial void OnSelectedScopeIndexChanged(int value) => Load();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedVariableChanged(EnvVariableItemViewModel? value)
    {
        EditValueText = value?.Value ?? "";
        RebuildEntries();
    }

    /// <summary>Reads the current scope from the registry and refreshes the list.</summary>
    public void Load()
    {
        try
        {
            _all = _service.GetVariables(CurrentScope)
                .Select(v => new EnvVariableItemViewModel(v))
                .ToList();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read {Scope} environment variables", CurrentScope);
            _all = [];
            ErrorMessage = Loc.F("EnvVar_ReadError", "Could not read environment variables: {0}", ex.Message);
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void Refresh() => Load();

    private void ApplyFilter()
    {
        var term = SearchText.Trim();
        var selectedName = SelectedVariable?.Name;

        Variables.Clear();
        foreach (var item in _all.Where(v => term.Length == 0 || v.Matches(term)))
        {
            Variables.Add(item);
        }

        // Keep the detail pane on the same variable across refreshes when it still matches.
        SelectedVariable = Variables.FirstOrDefault(v => v.Name == selectedName);
    }

    private void RebuildEntries()
    {
        Entries.Clear();
        if (SelectedVariable is not { IsMultiValued: true } variable)
        {
            return;
        }

        var parts = EnvPathAnalyzer.Split(variable.Value);
        var analysis = EnvPathAnalyzer.Analyze(parts);
        for (var i = 0; i < analysis.Count; i++)
        {
            Entries.Add(new EnvPathEntryViewModel(
                analysis[i].Value, !analysis[i].Exists, analysis[i].IsDuplicate, i, analysis.Count));
        }

        OnPropertyChanged(nameof(HasEntryWarnings));
    }

    /// <summary>Creates a variable or overwrites an existing one in the current scope.</summary>
    public async Task<bool> SaveVariableAsync(string name, string value)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Contains('='))
        {
            ErrorMessage = Loc.T("EnvVar_BadName", "Variable names must be non-empty and cannot contain '='.");
            return false;
        }

        var saved = await WriteAsync(
            () => _service.SetVariableAsync(CurrentScope, name, value),
            Loc.F("EnvVar_Saved", "{0} saved.", name));
        if (saved)
        {
            Load();
            SelectedVariable = Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        return saved;
    }

    /// <summary>Applies the single-value text editor to the selected variable.</summary>
    [RelayCommand]
    private async Task SaveValueAsync()
    {
        if (SelectedVariable is { } variable)
        {
            await SaveVariableAsync(variable.Name, EditValueText);
        }
    }

    public async Task DeleteVariableAsync(EnvVariableItemViewModel variable)
    {
        var deleted = await WriteAsync(
            () => _service.DeleteVariableAsync(variable.Scope, variable.Name),
            Loc.F("EnvVar_Deleted", "{0} deleted.", variable.Name));
        if (deleted)
        {
            Load();
        }
    }

    public Task MoveEntryAsync(EnvPathEntryViewModel entry, int delta) =>
        MutateEntriesAsync(parts =>
        {
            var target = entry.Index + delta;
            if (target < 0 || target >= parts.Count)
            {
                return false;
            }

            (parts[entry.Index], parts[target]) = (parts[target], parts[entry.Index]);
            return true;
        });

    public Task AddEntryAsync(string path) =>
        MutateEntriesAsync(parts =>
        {
            path = path.Trim();
            if (path.Length == 0)
            {
                return false;
            }

            parts.Add(path);
            return true;
        });

    public Task UpdateEntryAsync(EnvPathEntryViewModel entry, string newPath) =>
        MutateEntriesAsync(parts =>
        {
            newPath = newPath.Trim();
            if (newPath.Length == 0 || newPath == entry.Path)
            {
                return false;
            }

            parts[entry.Index] = newPath;
            return true;
        });

    public Task DeleteEntryAsync(EnvPathEntryViewModel entry) =>
        MutateEntriesAsync(parts =>
        {
            parts.RemoveAt(entry.Index);
            return true;
        });

    private async Task MutateEntriesAsync(Func<List<string>, bool> mutate)
    {
        if (SelectedVariable is not { } variable)
        {
            return;
        }

        var parts = EnvPathAnalyzer.Split(variable.Value).ToList();
        if (!mutate(parts))
        {
            return;
        }

        var newValue = EnvPathAnalyzer.Join(parts);
        var saved = await WriteAsync(
            () => _service.SetVariableAsync(variable.Scope, variable.Name, newValue),
            Loc.F("EnvVar_Saved", "{0} saved.", variable.Name));
        if (saved)
        {
            ApplySavedValue(variable, newValue);
        }
    }

    /// <summary>Removes every entry flagged missing or duplicate in a single save.</summary>
    [RelayCommand]
    private async Task RepairEntriesAsync()
    {
        if (SelectedVariable is not { } variable)
        {
            return;
        }

        var parts = EnvPathAnalyzer.Split(variable.Value).ToList();
        var analysis = EnvPathAnalyzer.Analyze(parts);
        var keep = parts.Where((_, i) => analysis[i] is { Exists: true, IsDuplicate: false }).ToList();
        var removed = parts.Count - keep.Count;
        if (removed == 0)
        {
            return;
        }

        var newValue = EnvPathAnalyzer.Join(keep);
        var saved = await WriteAsync(
            () => _service.SetVariableAsync(variable.Scope, variable.Name, newValue),
            Loc.F("EnvVar_Repaired", "{0} invalid or duplicate entries removed.", removed));
        if (saved)
        {
            ApplySavedValue(variable, newValue);
        }
    }

    private void ApplySavedValue(EnvVariableItemViewModel variable, string newValue)
    {
        variable.Value = newValue;
        EditValueText = newValue;
        RebuildEntries();
        OnPropertyChanged(nameof(ShowListEditor));
        OnPropertyChanged(nameof(ShowTextEditor));
        OnPropertyChanged(nameof(SelectedValueIsFolder));
    }

    public async Task ExportAsync(string filePath)
    {
        await WriteAsync(
            () => _service.ExportAsync(filePath),
            Loc.F("EnvVar_Exported", "Backup written to {0}.", filePath));
    }

    public Task<EnvVariableBackup> ReadBackupAsync(string filePath) => _service.ReadBackupAsync(filePath);

    public async Task RestoreAsync(EnvVariableBackup backup, bool restoreUser, bool restoreSystem)
    {
        var restored = await WriteAsync(
            async () =>
            {
                var count = await _service.RestoreAsync(backup, restoreUser, restoreSystem);
                StatusMessage = Loc.F("EnvVar_Restored", "{0} variables restored.", count);
            },
            statusMessage: null);
        if (restored)
        {
            Load();
        }
    }

    /// <summary>Runs a write operation with busy state, error handling, and a status message.</summary>
    private async Task<bool> WriteAsync(Func<Task> operation, string? statusMessage)
    {
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await operation();
            if (statusMessage is not null)
            {
                StatusMessage = statusMessage;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("EnvVar_ElevationDeclined", "The change was cancelled (administrator approval declined).");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Environment variable operation failed");
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
