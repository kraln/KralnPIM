using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace PIM.Sync.Graph;

public static class GraphClientFactory
{
    public static GraphServiceClient Create(GraphAuthProvider authProvider)
    {
        var tokenProvider = new BaseBearerTokenAuthenticationProvider(
            new DynamicTokenProvider(authProvider));
        return new GraphServiceClient(tokenProvider);
    }

    private sealed class DynamicTokenProvider(GraphAuthProvider authProvider) : IAccessTokenProvider
    {
        public AllowedHostsValidator AllowedHostsValidator { get; } = new();

        public async Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            return await authProvider.GetAccessTokenAsync(cancellationToken);
        }
    }
}
