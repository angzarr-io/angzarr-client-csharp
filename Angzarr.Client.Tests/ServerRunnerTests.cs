using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Angzarr.Client.Router;
using Angzarr.Client.Server;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Health.V1;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for the C# server runner — exercises <see cref="ServerHandle"/>
/// construction, the health-service wiring (audit #68 starts NOT_SERVING),
/// and the <see cref="GrpcStatusTranslator"/> mapping (audit #87 decode
/// error wrapping).
///
/// <para>Process-level server boot + supervisor-flip is covered through
/// the in-process <see cref="HealthServiceImpl"/> rather than spinning
/// up a real loopback gRPC server (which has flaky shutdown semantics
/// under xUnit). The cross-language contract is the publisher flow:
/// supervisor → IHealthStatusPublisher → HealthServiceImpl.</para>
/// </summary>
[Collection(nameof(EnvCollection))]
public class ServerRunnerTests : IDisposable
{
    private readonly List<EnvVarOverride> _clears = new();

    public ServerRunnerTests()
    {
        _clears.Add(EnvVarOverride.Clear(Readiness.EnvBusEndpoint));
        _clears.Add(EnvVarOverride.Clear("ANGZARR_BIND_ADDRESS"));
        _clears.Add(EnvVarOverride.Clear("TRANSPORT_TYPE"));
        _clears.Add(EnvVarOverride.Clear("UDS_BASE_PATH"));
        _clears.Add(EnvVarOverride.Clear("PORT"));
    }

    public void Dispose()
    {
        foreach (var c in _clears) c.Dispose();
    }

    // -----------------------------------------------------------------------
    // CreateServer — audit #68: start NOT_SERVING
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateServer_StartsHealthAtNotServing()
    {
        // Audit #68: prior to the supervisor's first SERVING tick, both the
        // overall ("") and per-kind names report NOT_SERVING.
        var router = new ProjectorRouter("proj-test");
        var handle = ServerRunner.CreateServer(
            Angzarr.ProjectorService.BindService(new ProjectorGrpcAdapter(router)),
            ServerRunner.HealthNameProjector);

        handle.Should().NotBeNull();
        handle.Address.Should().NotBeEmpty();

        var overallStatus = await handle.HealthService.Check(
            new HealthCheckRequest { Service = "" }, context: null!);
        overallStatus.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.NotServing);

        var kindStatus = await handle.HealthService.Check(
            new HealthCheckRequest { Service = ServerRunner.HealthNameProjector }, context: null!);
        kindStatus.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.NotServing);
    }

    [Fact]
    public async Task PublisherSetServing_FlipsHealthService()
    {
        var router = new ProjectorRouter("proj-test");
        var handle = ServerRunner.CreateServer(
            Angzarr.ProjectorService.BindService(new ProjectorGrpcAdapter(router)),
            ServerRunner.HealthNameProjector);

        await handle.Publisher.SetStatusAsync(ServerRunner.HealthNameProjector, HealthStatus.Serving);
        await handle.Publisher.SetStatusAsync("", HealthStatus.Serving);

        var overall = await handle.HealthService.Check(
            new HealthCheckRequest { Service = "" }, context: null!);
        overall.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);

        var kind = await handle.HealthService.Check(
            new HealthCheckRequest { Service = ServerRunner.HealthNameProjector }, context: null!);
        kind.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact]
    public async Task SupervisorRunningWithOkProbe_FlipsRegisteredHealthNamesToServing()
    {
        // End-to-end of the supervisor → publisher → health-service chain
        // without booting a real gRPC server. Mirrors Python's
        // tests/test_server.py + tests/test_readiness.py interaction.
        var router = new ProjectorRouter("proj-test");
        var handle = ServerRunner.CreateServer(
            Angzarr.ProjectorService.BindService(new ProjectorGrpcAdapter(router)),
            ServerRunner.HealthNameProjector);

        handle.TransportSignal.MarkBound();
        var probes = new List<IProbe> { new TransportProbe(handle.TransportSignal) };
        var names = new[] { "", ServerRunner.HealthNameProjector };

        using var cts = new CancellationTokenSource();
        var task = ReadinessSupervisor.RunAsync(
            probes, handle.Publisher, names,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100),
            cts.Token);

        // Wait for the supervisor to publish at least one tick.
        await WaitFor(async () =>
        {
            var resp = await handle.HealthService.Check(
                new HealthCheckRequest { Service = ServerRunner.HealthNameProjector }, context: null!);
            return resp.Status == HealthCheckResponse.Types.ServingStatus.Serving;
        }, TimeSpan.FromSeconds(2));

        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task SupervisorWithFailingProbe_KeepsHealthNotServing()
    {
        var router = new ProjectorRouter("proj-test");
        var handle = ServerRunner.CreateServer(
            Angzarr.ProjectorService.BindService(new ProjectorGrpcAdapter(router)),
            ServerRunner.HealthNameProjector);

        var probes = new List<IProbe> { new FailingProbe() };
        var names = new[] { "", ServerRunner.HealthNameProjector };

        using var cts = new CancellationTokenSource();
        var task = ReadinessSupervisor.RunAsync(
            probes, handle.Publisher, names,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100),
            cts.Token);

        await Task.Delay(50);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        var resp = await handle.HealthService.Check(
            new HealthCheckRequest { Service = ServerRunner.HealthNameProjector }, context: null!);
        resp.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.NotServing);
    }

    // -----------------------------------------------------------------------
    // GrpcStatusTranslator — decode error wrapping (audit #87)
    // -----------------------------------------------------------------------

    [Fact]
    public void StatusTranslator_WrapsInvalidProtobufAsInvalidArgument()
    {
        // Audit #87: the framework catches Google.Protobuf's
        // InvalidProtocolBufferException from the dispatch layer and
        // surfaces it as INVALID_ARGUMENT, matching Python's
        // `_parse_any` / `_unpack_any` semantics. Trigger a real parse
        // failure since `InvalidProtocolBufferException` has no public ctor.
        global::Google.Protobuf.InvalidProtocolBufferException? pbe = null;
        try
        {
            var any = new Any
            {
                TypeUrl = "type.googleapis.com/x",
                Value = global::Google.Protobuf.ByteString.CopyFrom(new byte[] { 0xff, 0xff }),
            };
            any.Unpack<Angzarr.UUID>();
        }
        catch (global::Google.Protobuf.InvalidProtocolBufferException caught)
        {
            pbe = caught;
        }
        pbe.Should().NotBeNull();
        var ex = GrpcStatusTranslator.ToRpcException(pbe!);
        ex.Status.StatusCode.Should().Be(StatusCode.InvalidArgument);
        // Spec MED-1.7: static canonical message replaces the prior
        // "malformed protobuf payload: {pbe.Message}" interpolation so the
        // cross-language `error.message == ANY_DECODE_FAILED` assertion
        // holds. The dynamic protobuf parser text now rides in trailers
        // (`x-angzarr-cause`) so debugging information is preserved.
        ex.Status.Detail.Should().Be(ErrorMessages.AnyDecodeFailed);
        var causeEntry = ex.Trailers.Get("x-angzarr-cause");
        causeEntry.Should().NotBeNull();
        var codeEntry = ex.Trailers.Get("x-angzarr-code");
        codeEntry.Should().NotBeNull();
        codeEntry!.Value.Should().Be(ErrorCodes.AnyDecodeFailed);
    }

    [Fact]
    public void StatusTranslator_MapsCommandRejectedPrecondition()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new CommandRejectedError("nope", "FAILED_PRECONDITION"));
        ex.Status.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public void StatusTranslator_MapsCommandRejectedNotFound()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new CommandRejectedError("missing", "NOT_FOUND"));
        ex.Status.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public void StatusTranslator_MapsCommandRejectedInvalidArgument()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new CommandRejectedError("bad input", "INVALID_ARGUMENT"));
        ex.Status.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public void StatusTranslator_MapsInvalidArgumentError()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new InvalidArgumentError("bad"));
        ex.Status.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public void StatusTranslator_MapsInvalidTimestampError()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new InvalidTimestampError("bad ts"));
        ex.Status.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public void StatusTranslator_MapsConnectionErrorAsUnavailable()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new ConnectionError("down"));
        ex.Status.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public void StatusTranslator_MapsTransportErrorAsUnavailable()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new TransportError("disconnected"));
        ex.Status.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public void StatusTranslator_DefaultsUnknownToInternal()
    {
        var ex = GrpcStatusTranslator.ToRpcException(new InvalidOperationException("oops"));
        ex.Status.StatusCode.Should().Be(StatusCode.Internal);
    }

    // -----------------------------------------------------------------------
    // Adapter integration with dispatch (audit #87 wrapping)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProjectorAdapter_TranslatesUnknownDomainToInvalidArgument()
    {
        // The router rejects an unknown domain with InvalidArgumentError;
        // the adapter must translate that to RpcException(INVALID_ARGUMENT).
        var router = new ProjectorRouter("proj-test"); // no domains registered
        var adapter = new ProjectorGrpcAdapter(router);
        var book = new Angzarr.EventBook { Cover = new Angzarr.Cover { Domain = "unknown" } };

        var act = () => adapter.Handle(book, context: null!);
        await act.Should().ThrowAsync<RpcException>()
            .Where(e => e.Status.StatusCode == StatusCode.InvalidArgument);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task WaitFor(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condition not met within " + timeout);
    }

    private sealed class FailingProbe : IProbe
    {
        public string Name => "failing";
        public Task<bool> CheckAsync() => Task.FromResult(false);
    }

    private sealed class EnvVarOverride : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        private EnvVarOverride(string name, string? previous) { _name = name; _previous = previous; }

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
