using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace PIM.Sync.Graph;

public static class GraphClientFactory
{
    public static GraphServiceClient Create(string accessToken)
    {
        var authProvider = new BaseBearerTokenAuthenticationProvider(
            new TokenProvider(accessToken));
        return new GraphServiceClient(authProvider);
    }

    private sealed class TokenProvider(string token) : IAccessTokenProvider
    {
        public AllowedHostsValidator AllowedHostsValidator { get; } = new();

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(token);
        }
    }
}
