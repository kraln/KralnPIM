using PIM.Sync.Google;

namespace PIM.Sync.Google.Tests;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public async Task WaitAsync_TokensAvailable_ReturnsImmediately()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 10, refillRate: 10);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync(1);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50, $"Expected immediate return, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitAsync_MultipleConsumptions_DecrementsBucket()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 5, refillRate: 0.1);

        // Consume all 5 tokens immediately
        for (int i = 0; i < 5; i++)
            await limiter.WaitAsync(1);

        // Bucket should be empty (or close to it)
        Assert.True(limiter.AvailableTokens < 1);
    }

    [Fact]
    public async Task WaitAsync_BucketEmpty_BlocksUntilRefill()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 1, refillRate: 100);

        // Drain the bucket
        await limiter.WaitAsync(1);

        // This should block briefly then succeed after refill
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync(1);
        sw.Stop();

        // With 100 tokens/sec, refilling 1 token takes ~10ms
        Assert.True(sw.ElapsedMilliseconds < 200, $"Took too long: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 1, refillRate: 0.01);

        // Drain the bucket
        await limiter.WaitAsync(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.WaitAsync(100, cts.Token));
    }

    [Fact]
    public async Task WaitAsync_LargeConsumption_DecrementsByUnits()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 100, refillRate: 0.1);

        await limiter.WaitAsync(50);

        Assert.True(limiter.AvailableTokens <= 50 + 1); // +1 for tiny refill during execution
    }

    [Fact]
    public void AvailableTokens_InitializedToMax()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 250, refillRate: 250);
        Assert.Equal(250, limiter.AvailableTokens);
    }

    [Fact]
    public async Task Refill_DoesNotExceedMaxTokens()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 10, refillRate: 10000);

        // Wait a bit for potential over-refill
        await Task.Delay(100);

        Assert.True(limiter.AvailableTokens <= 10);
    }

    [Fact]
    public async Task WaitAsync_DefaultUnits_ConsumesOne()
    {
        var limiter = new TokenBucketRateLimiter(maxTokens: 10, refillRate: 0.1);

        var before = limiter.AvailableTokens;
        await limiter.WaitAsync();
        var after = limiter.AvailableTokens;

        Assert.True(before - after >= 0.9, $"Expected ~1 token consumed, got {before - after}");
    }
}
