using DevHub.Core.Services;

namespace DevHub.App.ViewModels;

/// <summary>All forge accounts (one per supported forge). Singleton, shared by Settings and the dashboard.</summary>
public sealed class ForgeAccountsViewModel
{
    public ForgeAccountsViewModel(IEnumerable<IForgeService> services, ITokenStore tokens, ISettingsService settings)
    {
        Accounts = services
            .Select(service => new ForgeAccountViewModel(service, tokens, settings))
            .ToList();
    }

    public IReadOnlyList<ForgeAccountViewModel> Accounts { get; }

    public bool AnyConnected => Accounts.Any(a => a.IsConnected);

    /// <summary>The connected account serving this git host (e.g. "github.com"), or null.</summary>
    public ForgeAccountViewModel? ConnectedForHost(string host) =>
        Accounts.FirstOrDefault(a =>
            a.IsConnected && string.Equals(a.Host, host, StringComparison.OrdinalIgnoreCase));
}
