using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Angzarr.Client.Router;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.HealthCheck;
using Microsoft.Extensions.Logging;

namespace Angzarr.Client.Server;

/// <summary>
/// Bundle returned by <see cref="ServerRunner.CreateServer"/> carrying
/// everything the runner needs to start the server, drive readiness,
/// and shut down cleanly. Mirrors Python <c>ServerHandle</c> and the
/// Rust runner's call sites.
/// </summary>
public sealed class ServerHandle : IDisposable
{
    public global::Grpc.Core.Server Server { get; }
    public string Address { get; }
    public TransportSignal TransportSignal { get; }
    public HealthServiceImpl HealthService { get; }
    public HealthStatusPublisher Publisher { get; }

    public ServerHandle(
        global::Grpc.Core.Server server,
        string address,
        TransportSignal transportSignal,
        HealthServiceImpl healthService,
        HealthStatusPublisher publisher)
    {
        Server = server;
        Address = address;
        TransportSignal = transportSignal;
        HealthService = healthService;
        Publisher = publisher;
    }

    public void Dispose()
    {
        // Caller is responsible for ShutdownAsync; this is best-effort cleanup.
        try { Server.ShutdownAsync().Wait(TimeSpan.FromSeconds(2)); } catch { }
    }
}

/// <summary>
/// Adapter that bridges <see cref="ReadinessSupervisor"/> output to the
/// gRPC <see cref="HealthServiceImpl"/>. Keeps the supervisor decoupled
/// from the gRPC runtime so its tests don't require a real health server.
/// </summary>
public sealed class HealthStatusPublisher : IHealthStatusPublisher
{
    private readonly HealthServiceImpl _impl;

    public HealthStatusPublisher(HealthServiceImpl impl)
    {
        _impl = impl;
    }

    public Task SetStatusAsync(string serviceName, HealthStatus status)
    {
        var grpcStatus = status == HealthStatus.Serving
            ? HealthCheckResponse.Types.ServingStatus.Serving
            : HealthCheckResponse.Types.ServingStatus.NotServing;
        _impl.SetStatus(serviceName, grpcStatus);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Per-kind server runners — mirror Python's <c>run_command_handler_server</c>
/// / <c>run_saga_server</c> / etc. and Rust's
/// <c>server::run_command_handler_server</c> / etc.
///
/// <para>Each runner:</para>
/// <list type="number">
///   <item>Resolves transport via <see cref="ServerConfigEnv.GetTransportConfig"/>
///   (TCP or UDS).</item>
///   <item>Adds <c>grpc.health.v1.Health</c> alongside the kind-specific service,
///   starting at <c>NOT_SERVING</c> (audit #68).</item>
///   <item>Spawns a <see cref="ReadinessSupervisor"/> with the configured
///   probes; the supervisor flips health to <c>SERVING</c> once all probes
///   are green.</item>
///   <item>On shutdown, flips every registered health name back to
///   <c>NOT_SERVING</c> so K8s drains the pod (audit #83).</item>
/// </list>
/// </summary>
public static class ServerRunner
{
    // Fully-qualified gRPC service names — matched against Health.Check
    // and used as health-reporter keys. Match Rust's HEALTH_NAME_*.
    public const string HealthNameCommandHandler = "angzarr_client.proto.angzarr.CommandHandlerService";
    public const string HealthNameSaga = "angzarr_client.proto.angzarr.SagaService";
    public const string HealthNameProcessManager = "angzarr_client.proto.angzarr.ProcessManagerService";
    public const string HealthNameProjector = "angzarr_client.proto.angzarr.ProjectorService";
    public const string HealthNameUpcaster = "angzarr_client.proto.angzarr.UpcasterService";

    /// <summary>
    /// Build a fresh <see cref="global::Grpc.Core.Server"/> with the
    /// supplied service binding + a health servicer registered at
    /// <see cref="HealthStatus.NotServing"/> for both the overall service
    /// ("") and the explicit <paramref name="serviceName"/>.
    /// </summary>
    public static ServerHandle CreateServer(
        ServerServiceDefinition serviceDefinition,
        string serviceName)
    {
        var (transportType, address) = ServerConfigEnv.GetTransportConfig();

        var healthImpl = new HealthServiceImpl();
        // Audit #68: start at NOT_SERVING for both the empty (overall) name
        // and the explicit service name. The supervisor flips to SERVING
        // only once every probe passes.
        healthImpl.SetStatus("", HealthCheckResponse.Types.ServingStatus.NotServing);
        if (!string.IsNullOrEmpty(serviceName))
            healthImpl.SetStatus(serviceName, HealthCheckResponse.Types.ServingStatus.NotServing);

        var server = new global::Grpc.Core.Server
        {
            Services =
            {
                serviceDefinition,
                Health.BindService(healthImpl),
            },
        };

        var port = ParsePort(address);
        if (port > 0)
        {
            server.Ports.Add(new ServerPort("[::]", port, ServerCredentials.Insecure));
        }
        // Note: Grpc.Core's standalone server cannot bind to a Unix socket
        // directly. UDS requires the modern Kestrel transport; we expose
        // the resolved address so the caller can wire a Kestrel host when
        // running in UDS mode. Tests cover the resolver; production UDS
        // hosting is out of scope for this port pass.

        var signal = new TransportSignal();
        return new ServerHandle(
            server,
            address,
            signal,
            healthImpl,
            new HealthStatusPublisher(healthImpl));
    }

    private static int ParsePort(string address)
    {
        // Accept "[::]:50052", "0.0.0.0:9090", "127.0.0.1:8080", but
        // return -1 for "unix:..." (caller hosts UDS separately).
        if (address.StartsWith("unix:", StringComparison.Ordinal))
            return -1;
        var idx = address.LastIndexOf(':');
        if (idx < 0)
            return -1;
        return int.TryParse(address.Substring(idx + 1), out var p) ? p : -1;
    }

    /// <summary>
    /// Start a server with the configured readiness supervisor and run
    /// until <paramref name="cancellationToken"/> fires. Emits structured
    /// <c>server_started</c> / <c>server_shutdown</c> log lines with the
    /// same field shape as Python and Rust (audit #89).
    /// </summary>
    public static async Task RunAsync(
        ServerHandle handle,
        IReadOnlyList<IProbe> probes,
        IReadOnlyList<string> serviceNames,
        string domain,
        string serviceName,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        logger ??= NullLoggerProvider.NullLogger.Instance;
        var transportType = (Environment.GetEnvironmentVariable("TRANSPORT_TYPE") ?? "tcp").ToLowerInvariant();

        // Audit #89: cross-language log shape. Same event name + same
        // field set on Rust + Python + C# so operators see equivalent
        // records from pods of either language.
        logger.LogInformation(
            "server_started service={Service} name={Name} transport={Transport} address={Address}",
            serviceName, domain, transportType, handle.Address);

        handle.Server.Start();
        handle.TransportSignal.MarkBound();

        var (interval, timeout) = Readiness.ProbeConfigFromEnv();

        using var supervisorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var supervisorTask = ReadinessSupervisor.RunAsync(
            probes,
            handle.Publisher,
            serviceNames,
            interval,
            timeout,
            supervisorCts.Token);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* expected */ }
        finally
        {
            // Audit #83: two-phase shutdown.
            // 1. Cancel + await the supervisor so any in-flight SetStatus
            //    finishes before we publish the final state.
            // 2. Flip every registered health name to NOT_SERVING so K8s
            //    drains the pod.
            supervisorCts.Cancel();
            try { await supervisorTask.ConfigureAwait(false); } catch { }
            foreach (var name in serviceNames)
            {
                try
                {
                    await handle.Publisher.SetStatusAsync(name, HealthStatus.NotServing).ConfigureAwait(false);
                }
                catch { }
            }
            await handle.Server.ShutdownAsync().ConfigureAwait(false);

            logger.LogInformation(
                "server_shutdown service={Service} name={Name}",
                serviceName, domain);
        }
    }

    /// <summary>
    /// Run a command handler gRPC server.
    /// CH never emits cross-domain commands at the framework level —
    /// events flow back through the response. No output-domain probes.
    /// </summary>
    public static Task RunCommandHandlerServerAsync<TState, THandler>(
        CommandHandlerRouter<TState, THandler> router,
        string domain = "",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
        where TState : new()
        where THandler : ICommandHandlerDomainHandler<TState>
    {
        var adapter = new CommandHandlerGrpcAdapter<TState, THandler>(router);
        var handle = CreateServer(
            Angzarr.CommandHandlerService.BindService(adapter),
            HealthNameCommandHandler);

        var probes = new List<IProbe> { new TransportProbe(handle.TransportSignal) };
        // Bus probe is optional — only added when ANGZARR_BUS_ENDPOINT is set.
        var bus = BusProbe.FromEnv();
        if (bus != null) probes.Add(bus);

        var names = new[] { "", HealthNameCommandHandler };
        return RunAsync(handle, probes, names, domain, HealthNameCommandHandler, logger, cancellationToken);
    }

    /// <summary>
    /// Run a saga gRPC server.
    /// Audit #74: the target domain is added as an
    /// <see cref="OutputDomainProbe"/> for sync sagas (target-domain probes
    /// are added unconditionally; teams that want async-only readiness
    /// should configure <c>ANGZARR_BUS_ENDPOINT</c> instead).
    /// </summary>
    public static Task RunSagaServerAsync<THandler>(
        SagaRouter<THandler> router,
        string domain = "",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
        where THandler : ISagaDomainHandler
    {
        var adapter = new SagaGrpcAdapter<THandler>(router);
        var handle = CreateServer(
            Angzarr.SagaService.BindService(adapter),
            HealthNameSaga);

        var probes = new List<IProbe> { new TransportProbe(handle.TransportSignal) };
        if (!string.IsNullOrEmpty(router.TargetDomain))
            probes.Add(OutputDomainProbe.ForDomain(router.TargetDomain));
        var bus = BusProbe.FromEnv();
        if (bus != null) probes.Add(bus);

        var names = new[] { "", HealthNameSaga };
        return RunAsync(handle, probes, names, domain, HealthNameSaga, logger, cancellationToken);
    }

    /// <summary>
    /// Run a process manager gRPC server.
    /// </summary>
    public static Task RunProcessManagerServerAsync<TState>(
        ProcessManagerRouter<TState> router,
        string domain = "",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
        where TState : new()
    {
        var adapter = new ProcessManagerGrpcAdapter<TState>(router);
        var handle = CreateServer(
            Angzarr.ProcessManagerService.BindService(adapter),
            HealthNameProcessManager);

        var probes = new List<IProbe> { new TransportProbe(handle.TransportSignal) };
        var bus = BusProbe.FromEnv();
        if (bus != null) probes.Add(bus);

        var names = new[] { "", HealthNameProcessManager };
        return RunAsync(handle, probes, names, domain, HealthNameProcessManager, logger, cancellationToken);
    }

    /// <summary>
    /// Run a projector gRPC server.
    /// </summary>
    public static Task RunProjectorServerAsync(
        ProjectorRouter router,
        string domain = "",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = new ProjectorGrpcAdapter(router);
        var handle = CreateServer(
            Angzarr.ProjectorService.BindService(adapter),
            HealthNameProjector);

        var probes = new List<IProbe> { new TransportProbe(handle.TransportSignal) };
        var names = new[] { "", HealthNameProjector };
        return RunAsync(handle, probes, names, domain, HealthNameProjector, logger, cancellationToken);
    }

    /// <summary>
    /// Run an upcaster gRPC server. Reuses the existing
    /// <see cref="UpcasterGrpcHandler"/> bridge.
    /// </summary>
    public static Task RunUpcasterServerAsync(
        UpcasterRouter router,
        string domain = "",
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = new UpcasterGrpcHandler(router);
        var handle = CreateServer(
            Angzarr.UpcasterService.BindService(adapter),
            HealthNameUpcaster);

        var probes = new List<IProbe> { new TransportProbe(handle.TransportSignal) };
        var names = new[] { "", HealthNameUpcaster };
        return RunAsync(handle, probes, names, domain, HealthNameUpcaster, logger, cancellationToken);
    }
}

/// <summary>
/// Minimal in-namespace null logger to avoid a separate dep on
/// <c>Microsoft.Extensions.Logging.Abstractions</c> just for the
/// shared singleton.
/// </summary>
internal static class NullLoggerProvider
{
    internal sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
