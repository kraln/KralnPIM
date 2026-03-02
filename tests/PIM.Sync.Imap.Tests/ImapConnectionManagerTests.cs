using PIM.Sync.Imap;

namespace PIM.Sync.Imap.Tests;

public class ImapConnectionManagerTests
{
    [Fact]
    public void CalculateBackoff_FirstAttempt_Returns1000()
    {
        Assert.Equal(1000, ImapConnectionManager.CalculateBackoffMs(1));
    }

    [Fact]
    public void CalculateBackoff_SecondAttempt_Returns2000()
    {
        Assert.Equal(2000, ImapConnectionManager.CalculateBackoffMs(2));
    }

    [Fact]
    public void CalculateBackoff_ThirdAttempt_Returns4000()
    {
        Assert.Equal(4000, ImapConnectionManager.CalculateBackoffMs(3));
    }

    [Fact]
    public void CalculateBackoff_HighAttempt_CappedAt60000()
    {
        Assert.Equal(60_000, ImapConnectionManager.CalculateBackoffMs(10));
        Assert.Equal(60_000, ImapConnectionManager.CalculateBackoffMs(20));
    }

    [Theory]
    [InlineData(4, 8000)]
    [InlineData(5, 16000)]
    [InlineData(6, 32000)]
    public void CalculateBackoff_ExponentialProgression(int attempt, int expectedMs)
    {
        Assert.Equal(expectedMs, ImapConnectionManager.CalculateBackoffMs(attempt));
    }

    [Fact]
    public void Constructor_NullImapHost_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImapConnectionManager("", 993, true, "smtp.example.com", 587,
                "user", "pass", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }

    [Fact]
    public void Constructor_NullSmtpHost_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImapConnectionManager("imap.example.com", 993, true, "", 587,
                "user", "pass", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }

    [Fact]
    public void Constructor_NullUsername_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ImapConnectionManager("imap.example.com", 993, true, "smtp.example.com", 587,
                "", "pass", Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance));
    }

    [Fact]
    public void Constructor_ValidParams_DoesNotThrow()
    {
        var manager = new ImapConnectionManager(
            "imap.example.com", 993, true,
            "smtp.example.com", 587,
            "user", "pass",
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.False(manager.SupportsCondstore);
    }
}
