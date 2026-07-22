using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Serilog;

namespace DevHub.App.ViewModels;

/// <summary>Connection state of one forge account (GitHub or GitLab).</summary>
public sealed partial class ForgeAccountViewModel : ObservableObject
{
    private readonly IForgeService _service;
    private readonly ITokenStore _tokens;
    private readonly ISettingsService _settings;

    public ForgeAccountViewModel(IForgeService service, ITokenStore tokens, ISettingsService settings)
    {
        _service = service;
        _tokens = tokens;
        _settings = settings;
        TokenInput = "";

        // Trust the stored state at startup; API calls surface expired tokens later.
        if (_settings.Settings.ConnectedForgeLogins.TryGetValue(service.DisplayName, out var login)
            && _tokens.Read(service.DisplayName) is { } token)
        {
            Login = login;
            IsConnected = true;

            // The avatar itself is never persisted (only the login is), so it has to be
            // re-fetched on every launch or it stays blank until the user reconnects.
            _ = RefreshAvatarAsync(token);
        }
    }

    private async Task RefreshAvatarAsync(string token)
    {
        try
        {
            var user = await _service.GetCurrentUserAsync(token);
            if (user is not null)
            {
                AvatarUrl = user.AvatarUrl;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Refreshing the {Forge} avatar failed", DisplayName);
        }
    }

    public ForgeKind Kind => _service.Kind;

    public string DisplayName => _service.DisplayName;

    public string Host => _service.Host;

    public string TokenHelpUrl => _service.TokenHelpUrl;

    public string TokenScopeHint => _service.TokenScopeHint;

    public string IconFile => Kind == ForgeKind.GitHub ? "github-color.svg" : "gitlab-color.svg";

    public Microsoft.UI.Xaml.Media.Imaging.SvgImageSource IconImage =>
        new(new Uri($"ms-appx:///Assets/ToolIcons/{IconFile}"));

    public Uri TokenHelpUri => new(_service.TokenHelpUrl);

    /// <summary>Bound to the token PasswordBox; cleared after a successful connect.</summary>
    [ObservableProperty]
    public partial string TokenInput { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisconnected))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsConnected { get; set; }

    public bool IsDisconnected => !IsConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ViewingAsTooltip))]
    public partial string? Login { get; set; }

    /// <summary>Tooltip for the account's avatar, wherever it is shown outside Settings.</summary>
    public string ViewingAsTooltip => Loc.F("Forge_ViewingAs", "Viewing info as {0}", Login ?? "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarImage))]
    public partial string? AvatarUrl { get; set; }

    public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? AvatarImage =>
        AvatarUrl is { Length: > 0 } url ? new(new Uri(url)) : null;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    public string StatusText => IsConnected
        ? Loc.F("Forge_ConnectedAs", "Connected as {0}", Login ?? "")
        : Loc.T("Forge_ConnectHint", "Connect with a personal access token");

    [RelayCommand]
    private async Task SubmitTokenAsync()
    {
        if (await ConnectAsync(TokenInput))
        {
            TokenInput = "";
        }
    }

    /// <summary>Validates the token against the API, then stores it in the Credential Manager.</summary>
    public async Task<bool> ConnectAsync(string token)
    {
        token = token.Trim();
        if (token.Length == 0)
        {
            ErrorMessage = Loc.T("Forge_EmptyToken", "Paste a personal access token first.");
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var user = await _service.GetCurrentUserAsync(token);
            if (user is null)
            {
                ErrorMessage = Loc.F("Forge_TokenRejected", "{0} rejected this token. {1}", DisplayName, TokenScopeHint);
                return false;
            }

            _tokens.Write(DisplayName, token);
            _settings.Settings.ConnectedForgeLogins[DisplayName] = user.Login;
            await _settings.SaveAsync();

            Login = user.Login;
            AvatarUrl = user.AvatarUrl;
            IsConnected = true;
            Log.Information("Connected {Forge} account {Login}", DisplayName, user.Login);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Connecting the {Forge} account failed", DisplayName);
            ErrorMessage = Loc.F("Forge_Unreachable", "Could not reach {0}: {1}", DisplayName, ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        try
        {
            _tokens.Delete(DisplayName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Deleting the {Forge} token failed", DisplayName);
        }

        _settings.Settings.ConnectedForgeLogins.Remove(DisplayName);
        await _settings.SaveAsync();

        IsConnected = false;
        Login = null;
        AvatarUrl = null;
        ErrorMessage = null;
        Log.Information("Disconnected the {Forge} account", DisplayName);
    }

    /// <summary>One of my activity feeds; empty when disconnected or on error.</summary>
    public async Task<IReadOnlyList<ForgeItem>> GetFeedAsync(ForgeFeed feed)
    {
        if (!IsConnected || Login is null || _tokens.Read(DisplayName) is not { } token)
        {
            return [];
        }

        try
        {
            return await _service.GetFeedAsync(feed, token, Login);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Fetching the {Forge} {Feed} feed failed", DisplayName, feed);
            return [];
        }
    }

    /// <summary>Repository header facts; null when disconnected or unreachable.</summary>
    public async Task<ForgeRepoInfo?> GetRepositoryAsync(string repoFullName)
    {
        if (!IsConnected || _tokens.Read(DisplayName) is not { } token)
        {
            return null;
        }

        try
        {
            return await _service.GetRepositoryAsync(token, repoFullName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Fetching {Forge} repository {Repo} failed", DisplayName, repoFullName);
            return null;
        }
    }

    /// <summary>Open PRs/MRs of a repository; empty when disconnected or on error.</summary>
    public Task<IReadOnlyList<ForgeItem>> GetRepoPullRequestsAsync(string repoFullName) =>
        GuardedRepoCallAsync(repoFullName, _service.GetRepoPullRequestsAsync);

    /// <summary>Open issues of a repository; empty when disconnected or on error.</summary>
    public Task<IReadOnlyList<ForgeItem>> GetRepoIssuesAsync(string repoFullName) =>
        GuardedRepoCallAsync(repoFullName, _service.GetRepoIssuesAsync);

    /// <summary>Recent CI runs of a repository; empty when disconnected or on error.</summary>
    public async Task<IReadOnlyList<ForgeWorkflowRun>> GetRepoWorkflowRunsAsync(string repoFullName)
    {
        if (!IsConnected || _tokens.Read(DisplayName) is not { } token)
        {
            return [];
        }

        try
        {
            return await _service.GetRepoWorkflowRunsAsync(token, repoFullName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Fetching {Forge} CI runs for {Repo} failed", DisplayName, repoFullName);
            return [];
        }
    }

    /// <summary>
    /// Creates a repository under this account. Unlike the feed/read calls above, this is a
    /// user-triggered mutation (from the New Project wizard) so failures are not swallowed.
    /// </summary>
    public async Task<ForgeRepoInfo> CreateRepositoryAsync(string name, bool isPrivate)
    {
        if (!IsConnected || _tokens.Read(DisplayName) is not { } token)
        {
            throw new InvalidOperationException($"{DisplayName} is not connected.");
        }

        return await _service.CreateRepositoryAsync(token, name, isPrivate);
    }

    private async Task<IReadOnlyList<ForgeItem>> GuardedRepoCallAsync(
        string repoFullName,
        Func<string, string, CancellationToken, Task<IReadOnlyList<ForgeItem>>> call)
    {
        if (!IsConnected || _tokens.Read(DisplayName) is not { } token)
        {
            return [];
        }

        try
        {
            return await call(token, repoFullName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Fetching {Forge} items for {Repo} failed", DisplayName, repoFullName);
            return [];
        }
    }
}
