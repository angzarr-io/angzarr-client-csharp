using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Angzarr.Client.Server;
using FluentAssertions;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Mirrors Python <c>tests/test_readiness.py</c> and Rust
/// <c>src/readiness.rs</c> unit tests. Covers probe config from env,
/// the one-way <see cref="TransportSignal"/>, endpoint parsing,
/// <see cref="OutputDomainProbe"/>/<see cref="BusProbe"/> connect logic,
/// and the supervisor tick's aggregation + panic/timeout isolation
/// (audit #82).
/// </summary>
public class ReadinessTests
{
    // -----------------------------------------------------------------------
    // ProbeConfigFromEnv
    // -----------------------------------------------------------------------

    [Fact]
    public void ProbeConfigFromEnv_DefaultsWhenUnset()
    {
        using var _ = EnvVarOverride.Clear(Readiness.EnvInterval);
        using var __ = EnvVarOverride.Clear(Readiness.EnvTimeout);
        var (interval, timeout) = Readiness.ProbeConfigFromEnv();
        interval.Should().Be(Readiness.DefaultProbeInterval);
        timeout.Should().Be(Readiness.DefaultProbeTimeout);
    }

    [Fact]
    public void ProbeConfigFromEnv_ReadsEnv()
    {
        using var _ = EnvVarOverride.Set(Readiness.EnvInterval, "5");
        using var __ = EnvVarOverride.Set(Readiness.EnvTimeout, "1");
        var (interval, timeout) = Readiness.ProbeConfigFromEnv();
        interval.Should().Be(TimeSpan.FromSeconds(5));
        timeout.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ProbeConfigFromEnv_FallsBackOnGarbage()
    {
        using var _ = EnvVarOverride.Set(Readiness.EnvInterval, "thirty");
        using var __ = EnvVarOverride.Set(Readiness.EnvTimeout, "two");
        var (interval, timeout) = Readiness.ProbeConfigFromEnv();
        interval.Should().Be(Readiness.DefaultProbeInterval);
        timeout.Should().Be(Readiness.DefaultProbeTimeout);
    }

    // -----------------------------------------------------------------------
    // TransportProbe / TransportSignal
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TransportProbe_StartsUnbound()
    {
        var (probe, signal) = TransportProbe.New();
        probe.Name.Should().Be("transport");
        (await probe.CheckAsync()).Should().BeFalse();
        signal.IsBound.Should().BeFalse();
    }

    [Fact]
    public async Task TransportProbe_FlipsAfterSignalMarked()
    {
        var (probe, signal) = TransportProbe.New();
        signal.MarkBound();
        (await probe.CheckAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task TransportProbe_SignalIsOneWay()
    {
        var (probe, signal) = TransportProbe.New();
        signal.MarkBound();
        signal.IsBound.Should().BeTrue();
        (await probe.CheckAsync()).Should().BeTrue();
        // No public unmark; reusing the signal stays bound.
        signal.IsBound.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ParseEndpoint
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseEndpoint_TcpHostPort()
    {
        var ep = ReadinessEndpoint.Parse("inventory:1310");
        ep.IsTcp.Should().BeTrue();
        ep.Value.Should().Be("inventory:1310");
    }

    [Fact]
    public void ParseEndpoint_UnixPrefixIsUds()
    {
        var ep = ReadinessEndpoint.Parse("unix:/tmp/sock");
        ep.IsUds.Should().BeTrue();
        ep.Value.Should().Be("/tmp/sock");
    }

    [Fact]
    public void ParseEndpoint_UnixRelativeIsUds()
    {
        var ep = ReadinessEndpoint.Parse("unix:relative.sock");
        ep.IsUds.Should().BeTrue();
        ep.Value.Should().Be("relative.sock");
    }

    [Fact]
    public void ParseEndpoint_LeadingSlashIsUds()
    {
        var ep = ReadinessEndpoint.Parse("/var/run/angzarr.sock");
        ep.IsUds.Should().BeTrue();
        ep.Value.Should().Be("/var/run/angzarr.sock");
    }

    // -----------------------------------------------------------------------
    // OutputDomainProbe — TCP
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OutputDomainProbe_TcpCheckSucceedsWhenListenerBound()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new OutputDomainProbe("downstream", ReadinessEndpoint.Tcp($"127.0.0.1:{port}"));
            probe.Name.Should().Be("downstream");
            (await probe.CheckAsync()).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task OutputDomainProbe_TcpCheckFailsWhenNothingListening()
    {
        // Bind+release to grab an unused port, then probe it (nothing listening).
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var probe = new OutputDomainProbe("downstream", ReadinessEndpoint.Tcp($"127.0.0.1:{port}"));
        (await probe.CheckAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task OutputDomainProbe_TcpCheckFailsOnGarbageAddress()
    {
        var probe = new OutputDomainProbe("garbage", ReadinessEndpoint.Tcp("not-a-host:notaport"));
        (await probe.CheckAsync()).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // BusProbe.FromEnv
    // -----------------------------------------------------------------------

    [Fact]
    public void BusProbe_FromEnvReturnsNullWhenUnset()
    {
        using var _ = EnvVarOverride.Clear(Readiness.EnvBusEndpoint);
        BusProbe.FromEnv().Should().BeNull();
    }

    [Fact]
    public void BusProbe_FromEnvReturnsNullWhenBlank()
    {
        using var _ = EnvVarOverride.Set(Readiness.EnvBusEndpoint, "");
        BusProbe.FromEnv().Should().BeNull();
    }

    [Fact]
    public void BusProbe_FromEnvBuildsTcpProbe()
    {
        using var _ = EnvVarOverride.Set(Readiness.EnvBusEndpoint, "kafka:9092");
        var probe = BusProbe.FromEnv();
        probe.Should().NotBeNull();
        probe!.Name.Should().Be("bus");
    }

    // -----------------------------------------------------------------------
    // SupervisorTickAsync — aggregation + panic/timeout isolation (audit #82)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SupervisorTick_ReturnsTrueWhenAllProbesOk()
    {
        var probes = new List<IProbe> { new OkProbe(), new OkProbe() };
        var ok = await ReadinessSupervisor.TickAsync(probes, TimeSpan.FromMilliseconds(100));
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task SupervisorTick_ReturnsFalseWhenAnyProbeReturnsFalse()
    {
        var probes = new List<IProbe> { new OkProbe(), new BadProbe() };
        var ok = await ReadinessSupervisor.TickAsync(probes, TimeSpan.FromMilliseconds(100));
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task SupervisorTick_SurvivesThrowingProbeAndReturnsFalse()
    {
        // Audit #82: a probe that throws must not propagate up. Tick
        // returns false; supervisor lives.
        var probes = new List<IProbe>
        {
            new OkProbe(),
            new ThrowingProbe("boom"),
            new OkProbe(),
        };
        var ok = await ReadinessSupervisor.TickAsync(probes, TimeSpan.FromMilliseconds(100));
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task SupervisorTick_TreatsSlowProbeAsFailureViaTimeout()
    {
        var probes = new List<IProbe> { new SlowProbe(TimeSpan.FromMilliseconds(200)) };
        var ok = await ReadinessSupervisor.TickAsync(probes, TimeSpan.FromMilliseconds(20));
        ok.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // RunSupervisorAsync — end-to-end survival + status publishing
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RunSupervisor_LivesAcrossThrowingProbes()
    {
        // Audit #82 end-to-end: spawn supervisor with a throwing probe,
        // a short interval, and assert it's still running after a few ticks.
        var publisher = new RecordingStatusPublisher();
        var probes = new List<IProbe>
        {
            new ThrowingProbe("supervisor must survive this"),
        };

        using var cts = new CancellationTokenSource();
        var task = ReadinessSupervisor.RunAsync(
            probes,
            publisher,
            new[] { "svc" },
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(50),
            cts.Token
        );

        await Task.Delay(60);
        task.IsCompleted.Should().BeFalse("supervisor exited early — exception catch broke");
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        // While failing, status should have been published as NotServing.
        publisher.Statuses.Should().NotBeEmpty();
        publisher.Statuses.Should().AllSatisfy(s => s.Status.Should().Be(HealthStatus.NotServing));
    }

    [Fact]
    public async Task RunSupervisor_PublishesServingWhenAllProbesOk()
    {
        var publisher = new RecordingStatusPublisher();
        var probes = new List<IProbe> { new OkProbe() };

        using var cts = new CancellationTokenSource();
        var task = ReadinessSupervisor.RunAsync(
            probes,
            publisher,
            new[] { "", "svc" },
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(50),
            cts.Token
        );

        await Task.Delay(40);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        publisher.Statuses.Should().NotBeEmpty();
        publisher.Statuses.Should().AllSatisfy(s => s.Status.Should().Be(HealthStatus.Serving));
        publisher.ServiceNames.Should().Contain("").And.Contain("svc");
    }

    // -----------------------------------------------------------------------
    // Test helpers
    // -----------------------------------------------------------------------

    private sealed class OkProbe : IProbe
    {
        public string Name => "ok";
        public Task<bool> CheckAsync() => Task.FromResult(true);
    }

    private sealed class BadProbe : IProbe
    {
        public string Name => "bad";
        public Task<bool> CheckAsync() => Task.FromResult(false);
    }

    private sealed class ThrowingProbe : IProbe
    {
        private readonly string _msg;
        public ThrowingProbe(string msg) { _msg = msg; }
        public string Name => "thrower";
        public Task<bool> CheckAsync() => throw new InvalidOperationException(_msg);
    }

    private sealed class SlowProbe : IProbe
    {
        private readonly TimeSpan _delay;
        public SlowProbe(TimeSpan delay) { _delay = delay; }
        public string Name => "slow";
        public async Task<bool> CheckAsync()
        {
            await Task.Delay(_delay);
            return true;
        }
    }

    private sealed class RecordingStatusPublisher : IHealthStatusPublisher
    {
        private readonly object _lock = new();
        private readonly List<(string Service, HealthStatus Status)> _statuses = new();

        public IReadOnlyList<(string Service, HealthStatus Status)> Statuses
        {
            get { lock (_lock) { return _statuses.ToArray(); } }
        }

        public IEnumerable<string> ServiceNames
        {
            get { lock (_lock) { return new HashSet<string>(_statuses.ConvertAll(s => s.Service)); } }
        }

        public Task SetStatusAsync(string serviceName, HealthStatus status)
        {
            lock (_lock) { _statuses.Add((serviceName, status)); }
            return Task.CompletedTask;
        }
    }

    private sealed class EnvVarOverride : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        private EnvVarOverride(string name, string? previous)
        {
            _name = name;
            _previous = previous;
        }

        public static EnvVarOverride Set(string name, string value)
        {
            var prev = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            return new EnvVarOverride(name, prev);
        }

        public static EnvVarOverride Clear(string name)
        {
            var prev = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
            return new EnvVarOverride(name, prev);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
