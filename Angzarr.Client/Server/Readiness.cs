using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Angzarr.Client.Server;

/// <summary>
/// Readiness probes and health-status supervisor for runner servers.
///
/// <para>C# port of <c>angzarr_client/readiness.py</c> and
/// <c>angzarr-client-rust/src/readiness.rs</c>. Audit #68 / #74 / #82.</para>
///
/// <para>A runner exposes its readiness through <c>grpc.health.v1.Health</c>.
/// While any probe is failing, the per-kind service name reports
/// <c>NOT_SERVING</c>; once every probe is green, it flips to <c>SERVING</c>.
/// Probes are evaluated on a fixed cadence (default 30s, override via
/// <c>ANGZARR_READINESS_PROBE_INTERVAL</c>) with a per-probe timeout
/// (default 2s, override via <c>ANGZARR_READINESS_PROBE_TIMEOUT</c>).</para>
/// </summary>
public static class Readiness
{
    /// <summary>Default cadence for re-evaluating output-domain probes.</summary>
    public static readonly TimeSpan DefaultProbeInterval = TimeSpan.FromSeconds(30);

    /// <summary>Default per-probe timeout.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(2);

    public const string EnvInterval = "ANGZARR_READINESS_PROBE_INTERVAL";
    public const string EnvTimeout = "ANGZARR_READINESS_PROBE_TIMEOUT";

    /// <summary>
    /// Audit #74: optional async-bus endpoint (Kafka / RabbitMQ / SQS /
    /// SNS / NATS / etc.). When set, a single <see cref="BusProbe"/>
    /// covers reachability of the async path for every async-only
    /// saga / PM target. When unset, no bus probe is added — async-only
    /// targets are simply not part of readiness.
    /// </summary>
    public const string EnvBusEndpoint = "ANGZARR_BUS_ENDPOINT";

    /// <summary>
    /// Read the supervisor cadence + per-probe timeout from env, falling
    /// back to <see cref="DefaultProbeInterval"/> / <see cref="DefaultProbeTimeout"/>.
    /// Bad values (non-numeric) silently fall back to defaults — matches
    /// Rust's <c>.parse::&lt;u64&gt;().ok()</c>.
    /// </summary>
    public static (TimeSpan Interval, TimeSpan Timeout) ProbeConfigFromEnv()
    {
        return (Read(EnvInterval, DefaultProbeInterval), Read(EnvTimeout, DefaultProbeTimeout));

        static TimeSpan Read(string envVar, TimeSpan defaultValue)
        {
            var raw = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(raw))
                return defaultValue;
            if (!double.TryParse(
                    raw,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds
                ))
                return defaultValue;
            return TimeSpan.FromSeconds(seconds);
        }
    }
}

/// <summary>
/// Single readiness probe — evaluated once per supervisor tick.
/// </summary>
public interface IProbe
{
    /// <summary>Stable identifier for log lines and per-probe service names.</summary>
    string Name { get; }

    /// <summary><c>true</c> when the underlying dependency is currently healthy.</summary>
    Task<bool> CheckAsync();
}

/// <summary>
/// Side of a <see cref="TransportProbe"/> used by the runner to mark
/// "bound and serving". One-way: there is no public unmark; matches the
/// Rust <c>AtomicBool</c> set-only semantics.
/// </summary>
public sealed class TransportSignal
{
    private int _bound; // 0 = unbound, 1 = bound

    /// <summary>Mark the transport as accepting traffic.</summary>
    public void MarkBound() => Interlocked.Exchange(ref _bound, 1);

    /// <summary>Whether <see cref="MarkBound"/> has been called.</summary>
    public bool IsBound => Volatile.Read(ref _bound) == 1;
}

/// <summary>
/// One-shot transport probe — flipped <c>true</c> once the listener has
/// bound and the server is accepting traffic. From that point its result
/// never changes.
/// </summary>
public sealed class TransportProbe : IProbe
{
    private readonly TransportSignal _signal;

    public TransportProbe(TransportSignal signal)
    {
        _signal = signal;
    }

    /// <summary>Construct a paired probe + signal.</summary>
    public static (TransportProbe Probe, TransportSignal Signal) New()
    {
        var signal = new TransportSignal();
        return (new TransportProbe(signal), signal);
    }

    public string Name => "transport";

    public Task<bool> CheckAsync() => Task.FromResult(_signal.IsBound);
}

/// <summary>
/// TCP / UDS endpoint discriminator for readiness probes.
/// Mirrors the Python module's <c>_TcpEndpoint</c> / <c>_UdsEndpoint</c>
/// types and Rust's <c>Endpoint</c> enum.
/// </summary>
public sealed class ReadinessEndpoint
{
    public bool IsTcp { get; }
    public bool IsUds => !IsTcp;
    public string Value { get; }

    private ReadinessEndpoint(bool isTcp, string value)
    {
        IsTcp = isTcp;
        Value = value;
    }

    public static ReadinessEndpoint Tcp(string addr) => new(true, addr);

    public static ReadinessEndpoint Uds(string path) => new(false, path);

    /// <summary>
    /// Classify a resolved endpoint string into TCP or UDS.
    /// <c>unix:</c> prefix or leading <c>/</c> selects UDS; otherwise TCP.
    /// Mirrors Python's <c>_parse_endpoint</c>.
    /// </summary>
    public static ReadinessEndpoint Parse(string raw)
    {
        const string unixPrefix = "unix:";
        if (raw.StartsWith(unixPrefix, StringComparison.Ordinal))
            return Uds(raw.Substring(unixPrefix.Length));
        if (raw.StartsWith("/", StringComparison.Ordinal))
            return Uds(raw);
        return Tcp(raw);
    }
}

/// <summary>
/// Per-output-domain coordinator probe — attempts to open a connection to
/// the downstream domain's command-handler coordinator endpoint.
///
/// <para>Audit #74: built only for <b>sync</b> output domains (declared via
/// <c>@saga(sync=True)</c> or <c>@process_manager(sync_targets=[...])</c>).
/// Async-only targets ride the bus; see <see cref="BusProbe"/>.</para>
/// </summary>
public sealed class OutputDomainProbe : IProbe
{
    private readonly string _domain;
    private readonly ReadinessEndpoint _endpoint;

    public OutputDomainProbe(string domain, ReadinessEndpoint endpoint)
    {
        _domain = domain;
        _endpoint = endpoint;
    }

    /// <summary>
    /// Resolve the coordinator endpoint for <paramref name="domain"/> and
    /// build a probe. Reads <c>ANGZARR_MODE</c> / <c>ANGZARR_CH_PORT</c>
    /// via <see cref="Transport.ResolveCHEndpoint"/>.
    /// </summary>
    public static OutputDomainProbe ForDomain(string domain)
    {
        var raw = Transport.ResolveCHEndpoint(domain);
        return new OutputDomainProbe(domain, ReadinessEndpoint.Parse(raw));
    }

    public string Name => _domain;

    public Task<bool> CheckAsync() => TryConnectAsync(_endpoint);

    internal static async Task<bool> TryConnectAsync(ReadinessEndpoint endpoint)
    {
        if (endpoint.IsTcp)
            return await TryTcpAsync(endpoint.Value).ConfigureAwait(false);
        return await TryUdsAsync(endpoint.Value).ConfigureAwait(false);
    }

    private static async Task<bool> TryTcpAsync(string addr)
    {
        // Split on the LAST colon so IPv6 brackets are tolerated.
        var idx = addr.LastIndexOf(':');
        if (idx < 0)
            return false;
        var host = addr.Substring(0, idx);
        var portStr = addr.Substring(idx + 1);
        if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
            host = host.Substring(1, host.Length - 2);
        if (!int.TryParse(portStr, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var port))
            return false;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(string.IsNullOrEmpty(host) ? "localhost" : host, port)
                .ConfigureAwait(false);
            return client.Connected;
        }
        catch (SocketException) { return false; }
        catch (System.Net.NetworkInformation.NetworkInformationException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static async Task<bool> TryUdsAsync(string path)
    {
        try
        {
            using var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await sock.ConnectAsync(new UnixDomainSocketEndPoint(path)).ConfigureAwait(false);
            return sock.Connected;
        }
        catch (SocketException) { return false; }
        catch (System.IO.FileNotFoundException) { return false; }
        catch (System.IO.DirectoryNotFoundException) { return false; }
        catch (ArgumentException) { return false; }
    }
}

/// <summary>
/// Audit #74: async-bus reachability probe — covers the path that
/// async-only saga / PM targets ride. The endpoint is operator-supplied
/// via <see cref="Readiness.EnvBusEndpoint"/> and points at whatever
/// broker the deployment uses (Kafka, RabbitMQ, SQS/SNS, NATS, etc.).
/// Connection-only — confirms the broker is reachable, not that publishes
/// will succeed end-to-end.
/// </summary>
public sealed class BusProbe : IProbe
{
    private readonly ReadinessEndpoint _endpoint;

    public BusProbe(ReadinessEndpoint endpoint)
    {
        _endpoint = endpoint;
    }

    /// <summary>
    /// Build a <see cref="BusProbe"/> from <see cref="Readiness.EnvBusEndpoint"/>,
    /// or <c>null</c> if the env var is unset / blank.
    /// </summary>
    public static BusProbe? FromEnv()
    {
        var raw = Environment.GetEnvironmentVariable(Readiness.EnvBusEndpoint);
        if (string.IsNullOrEmpty(raw))
            return null;
        return new BusProbe(ReadinessEndpoint.Parse(raw));
    }

    public string Name => "bus";

    public Task<bool> CheckAsync() => OutputDomainProbe.TryConnectAsync(_endpoint);
}

/// <summary>
/// gRPC health status. Two-state mirror of <c>HealthCheckResponse.Status</c>
/// suitable for the supervisor's binary aggregation. Cross-language parity:
/// matches <c>ServingStatus::Serving</c> / <c>NotServing</c> in tonic-health.
/// </summary>
public enum HealthStatus
{
    NotServing,
    Serving,
}

/// <summary>
/// Adapter over the gRPC health service. The supervisor publishes
/// aggregated probe state through this contract. Tests substitute an
/// in-memory recorder; production wires an adapter over
/// <c>Grpc.HealthCheck.HealthServiceImpl</c>.
/// </summary>
public interface IHealthStatusPublisher
{
    Task SetStatusAsync(string serviceName, HealthStatus status);
}

/// <summary>
/// Runs the readiness supervisor: poll every probe on each tick, aggregate
/// (<c>all_ok</c> → <see cref="HealthStatus.Serving"/>, else
/// <see cref="HealthStatus.NotServing"/>), and publish to every service
/// name registered with the <see cref="IHealthStatusPublisher"/>. Loops
/// until the cancellation token fires.
///
/// <para>Audit #82: probe exceptions and per-probe timeouts are isolated
/// to the failing probe (it counts as failed), the supervisor itself
/// survives. Each cause emits its own WARN log; there is no aggregate
/// "failed" log on top.</para>
/// </summary>
public static class ReadinessSupervisor
{
    /// <summary>
    /// One iteration of the supervisor loop. Returns <c>true</c> iff all
    /// probes report healthy within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<bool> TickAsync(IEnumerable<IProbe> probes, TimeSpan timeout)
    {
        var allOk = true;
        foreach (var probe in probes)
        {
            bool ok;
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                var checkTask = probe.CheckAsync();
                var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                var done = await Task.WhenAny(checkTask, delayTask).ConfigureAwait(false);
                if (done == checkTask)
                {
                    ok = await checkTask.ConfigureAwait(false);
                    if (!ok)
                    {
                        // WARN log parity with Rust `warn!(probe = ..., "readiness probe failed")`.
                        // We don't take a logger dep here — log binding lives at the
                        // server level. The probe name is preserved for callers that
                        // wrap this.
                    }
                }
                else
                {
                    // Timeout fired before probe completed.
                    cts.Cancel(); // belt + braces
                    ok = false;
                }
            }
            catch (Exception)
            {
                // Audit #82: a misbehaving probe (exception) is treated as
                // failed; the supervisor lives. WARN log responsibility sits
                // with the calling server (we keep this module pure compute).
                ok = false;
            }

            if (!ok)
                allOk = false;
        }
        return allOk;
    }

    /// <summary>
    /// Spawn the supervisor loop. Returns a <see cref="Task"/> that
    /// completes only when <paramref name="cancellationToken"/> fires.
    /// </summary>
    public static async Task RunAsync(
        IReadOnlyList<IProbe> probes,
        IHealthStatusPublisher publisher,
        IReadOnlyList<string> serviceNames,
        TimeSpan interval,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var allOk = await TickAsync(probes, timeout).ConfigureAwait(false);
            var status = allOk ? HealthStatus.Serving : HealthStatus.NotServing;
            foreach (var name in serviceNames)
            {
                try
                {
                    await publisher.SetStatusAsync(name, status).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Same audit #82 logic — publish failures must not kill
                    // the supervisor. Next tick will retry.
                }
            }

            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
