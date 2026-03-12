namespace PIM.Core.Providers;

/// <summary>
/// Thrown when a provider's stored credentials have been revoked or expired
/// and the user must re-authorize interactively.
/// </summary>
public sealed class ReauthorizationRequiredException : Exception
{
    public string AccountId { get; }

    public ReauthorizationRequiredException(string accountId, Exception? innerException = null)
        : base($"Account '{accountId}' requires re-authorization.", innerException)
    {
        AccountId = accountId;
    }
}
