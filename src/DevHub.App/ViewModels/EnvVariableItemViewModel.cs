using CommunityToolkit.Mvvm.ComponentModel;
using DevHub.Core.Models;
using DevHub.Core.Services;

namespace DevHub.App.ViewModels;

/// <summary>One row of the variables list; the value updates in place after edits.</summary>
public sealed partial class EnvVariableItemViewModel : ObservableObject
{
    public EnvVariableItemViewModel(EnvVariable variable)
    {
        Name = variable.Name;
        Scope = variable.Scope;
        Value = variable.Value;
    }

    public string Name { get; }

    public EnvVariableScope Scope { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultiValued))]
    public partial string Value { get; set; }

    /// <summary>True when the variable is edited as a list of semicolon-delimited entries.</summary>
    public bool IsMultiValued => EnvPathAnalyzer.IsMultiValued(Name, Value);

    public bool Matches(string term) =>
        Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
