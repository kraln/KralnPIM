namespace PIM.Core.Models;

public sealed record OAuthToken(
    string AccountId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt
);
