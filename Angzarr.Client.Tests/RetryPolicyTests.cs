using FluentAssertions;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for <see cref="ExponentialBackoffRetry"/> — mirrors Rust's
/// <c>retry::tests</c> module and Python's <c>retry</c> tests.
/// </summary>
public class RetryPolicyTests
{
    [Fact]
    public void Default_MatchesCrossLanguageSpec()
    {
        // 10 attempts, 100ms-5s, jitter enabled.
        var p = new ExponentialBackoffRetry();
        p.ComputeDelay(0).Should().BeInRange(50, 100);
    }

    [Fact]
    public void Execute_ReturnsAfterFirstSuccess()
    {
        var counter = 0;
        var policy = new ExponentialBackoffRetry(1, 10, 5, jitter: false);

        policy.Execute(() =>
        {
            counter++;
            if (counter < 3)
                throw new InvalidOperationException("not yet");
        });

        counter.Should().Be(3);
    }

    [Fact]
    public void Execute_ThrowsLastErrorWhenAllAttemptsFail()
    {
        var counter = 0;
        var policy = new ExponentialBackoffRetry(1, 10, 3, jitter: false);

        Action act = () =>
            policy.Execute(() =>
            {
                counter++;
                throw new InvalidOperationException($"fail-{counter}");
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("fail-3");
        counter.Should().Be(3);
    }

    [Fact]
    public void Execute_StopsAfterFirstSuccess()
    {
        var counter = 0;
        var policy = new ExponentialBackoffRetry(1, 10, 5, jitter: false);

        policy.Execute(() => counter++);

        counter.Should().Be(1);
    }

    [Fact]
    public void OnRetry_FiresBetweenAttemptsOnly()
    {
        // max_attempts=3: callback fires after attempts 0 and 1, not after
        // attempt 2 (the last). Matches Rust's `on_retry_fires_between_attempts_only`.
        var fired = 0;
        var policy = new ExponentialBackoffRetry(
            minDelayMs: 1,
            maxDelayMs: 10,
            maxAttempts: 3,
            jitter: false,
            onRetry: (attempt, _) => fired++
        );

        Action act = () => policy.Execute(() => throw new InvalidOperationException("nope"));

        act.Should().Throw<InvalidOperationException>();
        fired.Should().Be(2);
    }

    [Fact]
    public void ComputeDelay_CapsAtMaxDelay()
    {
        var policy = new ExponentialBackoffRetry(100, 1000, 10, jitter: false);
        policy.ComputeDelay(20).Should().Be(1000);
    }
}
