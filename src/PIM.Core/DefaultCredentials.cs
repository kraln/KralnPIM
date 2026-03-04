namespace PIM.Core;

/// <summary>
/// Embedded OAuth application credentials for zero-setup end-user experience.
/// These are "installed app" credentials — the client secret is not truly secret
/// per Google's documentation for desktop/CLI applications.
/// </summary>
public static class DefaultCredentials
{
    public static class Google
    {
        public const string ClientId = "119328042002-icgkdl38o57qjvhrhshs69hk3uhnebdk.apps.googleusercontent.com";
        public const string ClientSecret = "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";
    }

    public static class Office365
    {
        public const string ClientId = "43747840-9a36-45d3-b138-f673115ca94c";
        public const string TenantId = "common"; // Multi-tenant + personal accounts
    }
}
