namespace DevHub.Core.Services;

/// <summary>
/// Secure storage for secrets (forge tokens). Backed by the Windows Credential
/// Manager — secrets never land in settings.json or any other file DevHub writes.
/// </summary>
public interface ITokenStore
{
    /// <summary>The stored secret, or null when none exists for this key.</summary>
    string? Read(string key);

    void Write(string key, string secret);

    /// <summary>Removes the secret; no-op when the key does not exist.</summary>
    void Delete(string key);
}
