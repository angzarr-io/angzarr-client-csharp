using System;
using System.Threading;

namespace Angzarr.Client;

/// <summary>
/// Strategy for retrying failed operations.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Run the operation, retrying on failure according to the policy.
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    void Execute(Action operation);
}

/// <summary>
/// Retries with exponential backoff and optional jitter.
///
/// <para>Default configuration matches Rust's backoff:
/// 10 attempts, 100ms-5s delay, with jitter.</para>
/// </summary>
public class ExponentialBackoffRetry : IRetryPolicy
{
    private readonly int _minDelayMs;
    private readonly int _maxDelayMs;
    private readonly int _maxAttempts;
    private readonly bool _jitter;
    private static readonly Random _random = new();

    /// <summary>
    /// Create with default configuration (10 attempts, 100ms-5s, jitter enabled).
    /// </summary>
    public ExponentialBackoffRetry()
        : this(100, 5000, 10, true) { }

    /// <summary>
    /// Create with custom configuration.
    /// </summary>
    public ExponentialBackoffRetry(int minDelayMs, int maxDelayMs, int maxAttempts, bool jitter)
    {
        _minDelayMs = minDelayMs;
        _maxDelayMs = maxDelayMs;
        _maxAttempts = maxAttempts;
        _jitter = jitter;
    }

    public void Execute(Action operation)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception e)
            {
                lastError = e;
                if (attempt < _maxAttempts - 1)
                {
                    Thread.Sleep(ComputeDelay(attempt));
                }
            }
        }
        throw lastError!;
    }

    private int ComputeDelay(int attempt)
    {
        double delay = _minDelayMs * Math.Pow(2, attempt);
        delay = Math.Min(delay, _maxDelayMs);
        if (_jitter)
        {
            delay *= 0.5 + _random.NextDouble() * 0.5;
        }
        return (int)delay;
    }

    /// <summary>
    /// Returns the default retry policy matching Rust's backoff config.
    /// </summary>
    public static IRetryPolicy Default() => new ExponentialBackoffRetry();
}
