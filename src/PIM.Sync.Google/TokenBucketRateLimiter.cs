namespace PIM.Sync.Google;

public sealed class TokenBucketRateLimiter
{
    private readonly int _maxTokens;
    private readonly double _refillRate; // tokens per second
    private double _tokens;
    private long _lastRefillTicks;
    private readonly object _lock = new();

    public TokenBucketRateLimiter(int maxTokens = 250, double refillRate = 250.0)
    {
        _maxTokens = maxTokens;
        _refillRate = refillRate;
        _tokens = maxTokens;
        _lastRefillTicks = Environment.TickCount64;
    }

    public async Task WaitAsync(int units = 1, CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            TimeSpan delay;
            lock (_lock)
            {
                Refill();
                if (_tokens >= units)
                {
                    _tokens -= units;
                    return;
                }

                var deficit = units - _tokens;
                delay = TimeSpan.FromSeconds(deficit / _refillRate);
            }

            await Task.Delay(delay, ct);
        }
    }

    private void Refill()
    {
        var now = Environment.TickCount64;
        var elapsed = (now - _lastRefillTicks) / 1000.0;
        _lastRefillTicks = now;

        _tokens = Math.Min(_maxTokens, _tokens + elapsed * _refillRate);
    }

    // Visible for testing
    internal double AvailableTokens
    {
        get
        {
            lock (_lock)
            {
                Refill();
                return _tokens;
            }
        }
    }
}
