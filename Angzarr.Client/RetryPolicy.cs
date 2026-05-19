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

    /// <summary>
    /// Run a value-returning operation, retrying on failure according to
    /// the policy. Spec HIGH-6.2 — cross-language parity with Rs
    /// <c>policy.execute(|| ...)</c>, Py <c>policy.execute(callable)</c>,
    /// Ja <c>policy.execute(Callable&lt;T&gt;)</c>.
    /// </summary>
    T Execute<T>(Func<T> operation);
}

/// <summary>
/// Retries with exponential backoff and optional jitter.
///
/// <para>Default configuration matches Rust's backoff:
/// 10 attempts, 100ms-5s delay, with jitter.</para>
///
/// <para>Mirrors Rust's <c>ExponentialBackoffRetry</c> (with <c>on_retry</c>
/// callback) and Python's <c>ExponentialBackoffRetry</c>.</para>
/// </summary>
public class ExponentialBackoffRetry : IRetryPolicy
{
    private readonly int _minDelayMs;
    private readonly int _maxDelayMs;
    private readonly int _maxAttempts;
    private readonly bool _jitter;
    private readonly Action<int, Exception>? _onRetry;

    // Spec MED-6.5: thread-safe shared Random. `Random.Shared` (.NET 6+) is
    // designed for concurrent use; the previous `new Random()` was not safe
    // for parallel ComputeDelay invocations and could produce duplicate
    // values or corrupt internal state under load.
    private static readonly Random _random = Random.Shared;

    /// <summary>
    /// Create with default configuration (10 attempts, 100ms-5s, jitter enabled).
    /// </summary>
    public ExponentialBackoffRetry()
        : this(100, 5000, 10, true, onRetry: null) { }

    /// <summary>
    /// Create with custom configuration.
    /// </summary>
    public ExponentialBackoffRetry(int minDelayMs, int maxDelayMs, int maxAttempts, bool jitter)
        : this(minDelayMs, maxDelayMs, maxAttempts, jitter, onRetry: null) { }

    /// <summary>
    /// Create with custom configuration and an <c>onRetry</c> callback.
    ///
    /// <para>The callback fires before each backoff sleep — i.e. before
    /// attempts 2..N when at least one previous attempt failed. It is
    /// NOT fired before the first attempt nor after the last failed
    /// attempt. Mirrors Rust's <c>with_on_retry</c> and Python's
    /// <c>on_retry</c> hook.</para>
    /// </summary>
    public ExponentialBackoffRetry(
        int minDelayMs,
        int maxDelayMs,
        int maxAttempts,
        bool jitter,
        Action<int, Exception>? onRetry
    )
    {
        _minDelayMs = minDelayMs;
        _maxDelayMs = maxDelayMs;
        _maxAttempts = maxAttempts;
        _jitter = jitter;
        _onRetry = onRetry;
    }

    public void Execute(Action operation)
    {
        Execute<object?>(() => { operation(); return null; });
    }

    /// <summary>
    /// Run a value-returning operation, retrying on failure. Spec HIGH-6.2.
    /// </summary>
    public T Execute<T>(Func<T> operation)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception e)
            {
                lastError = e;
                if (attempt < _maxAttempts - 1)
                {
                    _onRetry?.Invoke(attempt, e);
                    Thread.Sleep(ComputeDelay(attempt));
                }
            }
        }
        throw lastError!;
    }

    /// <summary>
    /// Compute the delay for the given zero-indexed attempt number.
    /// Public for cross-language test parity (mirrors Rust's
    /// <c>RetryPolicy::compute_delay</c>).
    /// </summary>
    public int ComputeDelay(int attempt)
    {
        double delay = _minDelayMs * Math.Pow(2, attempt);
        delay = Math.Min(delay, _maxDelayMs);
        if (_jitter)
        {
            // Jitter factor in [0.5, 1.0) — matches Rust's
            // `0.5 + frac * 0.5` and Python's same formula.
            delay *= 0.5 + _random.NextDouble() * 0.5;
        }
        return (int)delay;
    }

    /// <summary>
    /// Returns the default retry policy matching Rust's backoff config.
    /// </summary>
    public static IRetryPolicy Default() => new ExponentialBackoffRetry();
}
