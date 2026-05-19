using System;
using System.IO;
using Angzarr.Client.Server;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Mirrors Python <c>tests/test_server.py</c> for the cross-language
/// transport-config / bind-address / cleanup helpers. Process-level
/// asyncio + grpc.aio spin-up isn't part of this port; the surface we
/// pin is the pure-compute resolvers + the <c>configure_logging</c> hook.
/// </summary>
[Collection(nameof(EnvCollection))]
public class ServerTests : IDisposable
{
    private readonly EnvVarOverride[] _cleared;

    public ServerTests()
    {
        _cleared = new[]
        {
            EnvVarOverride.Clear("TRANSPORT_TYPE"),
            EnvVarOverride.Clear("UDS_BASE_PATH"),
            EnvVarOverride.Clear("SERVICE_NAME"),
            EnvVarOverride.Clear("DOMAIN"),
            EnvVarOverride.Clear("SAGA_NAME"),
            EnvVarOverride.Clear("PROJECTOR_NAME"),
            EnvVarOverride.Clear("PORT"),
            EnvVarOverride.Clear(ServerConfigEnv.EnvBindAddress),
        };
    }

    public void Dispose()
    {
        foreach (var o in _cleared) o.Dispose();
    }

    // -----------------------------------------------------------------------
    // GetTransportConfig — TCP + UDS + ANGZARR_BIND_ADDRESS (audit #77)
    // -----------------------------------------------------------------------

    [Fact]
    public void GetTransportConfig_DefaultTcpDefaultPort()
    {
        var (transport, address) = ServerConfigEnv.GetTransportConfig();
        transport.Should().Be("tcp");
        address.Should().Be("[::]:50052");
    }

    [Fact]
    public void GetTransportConfig_TcpCustomPort()
    {
        using var _ = EnvVarOverride.Set("PORT", "6000");
        var (transport, address) = ServerConfigEnv.GetTransportConfig();
        transport.Should().Be("tcp");
        address.Should().Be("[::]:6000");
    }

    [Fact]
    public void GetTransportConfig_UdsWithoutQualifier()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "uds");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            var (transport, address) = ServerConfigEnv.GetTransportConfig();
            transport.Should().Be("uds");
            address.Should().Be($"unix:{tmp}/business.sock");
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void GetTransportConfig_UdsWithDomainQualifier()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "uds");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            using var ___ = EnvVarOverride.Set("SERVICE_NAME", "business");
            using var ____ = EnvVarOverride.Set("DOMAIN", "orders");
            var (_, address) = ServerConfigEnv.GetTransportConfig();
            address.Should().Be($"unix:{tmp}/business-orders.sock");
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void GetTransportConfig_UdsSagaNameFallback()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "uds");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            using var ___ = EnvVarOverride.Set("SERVICE_NAME", "saga");
            using var ____ = EnvVarOverride.Set("SAGA_NAME", "order-fulfillment");
            var (_, address) = ServerConfigEnv.GetTransportConfig();
            address.Should().Be($"unix:{tmp}/saga-order-fulfillment.sock");
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void GetTransportConfig_UdsProjectorNameFallback()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "uds");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            using var ___ = EnvVarOverride.Set("SERVICE_NAME", "projector");
            using var ____ = EnvVarOverride.Set("PROJECTOR_NAME", "output");
            var (_, address) = ServerConfigEnv.GetTransportConfig();
            address.Should().Be($"unix:{tmp}/projector-output.sock");
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void GetTransportConfig_UdsRemovesStaleSocket()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "uds");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            var stale = Path.Combine(tmp, "business.sock");
            File.WriteAllText(stale, "stale");
            File.Exists(stale).Should().BeTrue();
            ServerConfigEnv.GetTransportConfig();
            File.Exists(stale).Should().BeFalse();
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void GetTransportConfig_UdsCreatesParentDirectory()
    {
        var tmp = TempDir();
        try
        {
            var nested = Path.Combine(tmp, "nested", "sockets");
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "uds");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", nested);
            ServerConfigEnv.GetTransportConfig();
            Directory.Exists(nested).Should().BeTrue();
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void GetTransportConfig_TransportTypeCaseInsensitive()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "UDS");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            var (transport, _) = ServerConfigEnv.GetTransportConfig();
            transport.Should().Be("uds");
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void GetTransportConfig_BindAddressEnvOverride()
    {
        using var _ = EnvVarOverride.Set(ServerConfigEnv.EnvBindAddress, "0.0.0.0:9090");
        var (transport, address) = ServerConfigEnv.GetTransportConfig();
        transport.Should().Be("tcp");
        address.Should().Be("0.0.0.0:9090");
    }

    [Fact]
    public void GetTransportConfig_BindAddressOverrideIgnoresPort()
    {
        using var _ = EnvVarOverride.Set("PORT", "6000");
        using var __ = EnvVarOverride.Set(ServerConfigEnv.EnvBindAddress, "127.0.0.1:7777");
        var (_, address) = ServerConfigEnv.GetTransportConfig();
        address.Should().Be("127.0.0.1:7777");
    }

    [Fact]
    public void GetTransportConfig_BindAddressUdsUnaffected()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("TRANSPORT_TYPE", "uds");
            using var __ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            using var ___ = EnvVarOverride.Set(ServerConfigEnv.EnvBindAddress, "0.0.0.0:9090");
            var (transport, address) = ServerConfigEnv.GetTransportConfig();
            transport.Should().Be("uds");
            address.Should().StartWith("unix:");
        }
        finally { Cleanup(tmp); }
    }

    // -----------------------------------------------------------------------
    // ResolveBindAddress — audit #77 helper exposed at the public root
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveBindAddress_DefaultIsDualStack()
    {
        ServerConfigEnv.ResolveBindAddress().Should().Be("[::]:50052");
    }

    [Fact]
    public void ResolveBindAddress_DefaultWithExplicitPort()
    {
        ServerConfigEnv.ResolveBindAddress(8080).Should().Be("[::]:8080");
    }

    [Fact]
    public void ResolveBindAddress_PortEnvBeatsDefaultPortArg()
    {
        using var _ = EnvVarOverride.Set("PORT", "6000");
        ServerConfigEnv.ResolveBindAddress(50052).Should().Be("[::]:6000");
    }

    [Fact]
    public void ResolveBindAddress_EnvOverrideVerbatimIpv4()
    {
        using var _ = EnvVarOverride.Set(ServerConfigEnv.EnvBindAddress, "127.0.0.1:9090");
        ServerConfigEnv.ResolveBindAddress(50052).Should().Be("127.0.0.1:9090");
    }

    [Fact]
    public void ResolveBindAddress_EnvOverrideVerbatimIpv6()
    {
        using var _ = EnvVarOverride.Set(ServerConfigEnv.EnvBindAddress, "[::1]:8080");
        ServerConfigEnv.ResolveBindAddress(50052).Should().Be("[::1]:8080");
    }

    [Fact]
    public void ResolveBindAddress_EnvOverrideSupersedesPort()
    {
        using var _ = EnvVarOverride.Set("PORT", "6000");
        using var __ = EnvVarOverride.Set(ServerConfigEnv.EnvBindAddress, "0.0.0.0:1234");
        ServerConfigEnv.ResolveBindAddress().Should().Be("0.0.0.0:1234");
    }

    // -----------------------------------------------------------------------
    // ServerConfig.FromEnv — Rust-style ServerConfig type (audit cross-lang)
    // -----------------------------------------------------------------------

    [Fact]
    public void ServerConfig_FromEnv_TcpDefault()
    {
        var cfg = ServerConfig.FromEnv();
        cfg.Port.Should().Be(50052);
        cfg.UdsPath.Should().BeNull();
    }

    [Fact]
    public void ServerConfig_FromEnv_PortOverride()
    {
        using var _ = EnvVarOverride.Set("PORT", "7777");
        ServerConfig.FromEnv().Port.Should().Be(7777);
    }

    [Fact]
    public void ServerConfig_FromEnv_GrpcPortFallback()
    {
        using var _ = EnvVarOverride.Set("GRPC_PORT", "8888");
        ServerConfig.FromEnv().Port.Should().Be(8888);
    }

    [Fact]
    public void ServerConfig_FromEnv_UdsModeWhenAllThreeSet()
    {
        var tmp = TempDir();
        try
        {
            using var _ = EnvVarOverride.Set("UDS_BASE_PATH", tmp);
            using var __ = EnvVarOverride.Set("SERVICE_NAME", "business");
            using var ___ = EnvVarOverride.Set("DOMAIN", "orders");
            var cfg = ServerConfig.FromEnv();
            cfg.UdsPath.Should().Be(Path.Combine(tmp, "business-orders.sock"));
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void ServerConfig_FromEnv_GarbagePortFallsBackToDefault()
    {
        using var _ = EnvVarOverride.Set("PORT", "not-a-port");
        ServerConfig.FromEnv(default_port: 12345).Port.Should().Be(12345);
    }

    // -----------------------------------------------------------------------
    // CleanupSocket
    // -----------------------------------------------------------------------

    [Fact]
    public void CleanupSocket_RemovesExistingFile()
    {
        var tmp = TempDir();
        try
        {
            var path = Path.Combine(tmp, "x.sock");
            File.WriteAllText(path, "x");
            File.Exists(path).Should().BeTrue();
            AngzarrServer.CleanupSocket(path);
            File.Exists(path).Should().BeFalse();
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void CleanupSocket_NoOpWhenMissing()
    {
        var tmp = TempDir();
        try
        {
            var path = Path.Combine(tmp, "missing.sock");
            // No throw.
            AngzarrServer.CleanupSocket(path);
            File.Exists(path).Should().BeFalse();
        }
        finally { Cleanup(tmp); }
    }

    [Fact]
    public void CleanupSocket_NoOpWhenEmptyOrNull()
    {
        AngzarrServer.CleanupSocket("");
        AngzarrServer.CleanupSocket(null!);
    }

    // -----------------------------------------------------------------------
    // ConfigureLogging — idempotent + returns a usable logger factory
    // -----------------------------------------------------------------------

    [Fact]
    public void ConfigureLogging_ReturnsLoggerFactory()
    {
        using var factory = AngzarrServer.ConfigureLogging();
        factory.Should().NotBeNull();
        var logger = factory.CreateLogger("test");
        logger.Should().NotBeNull();
        // No throw on first log line.
        logger.LogInformation("ping");
    }

    [Fact]
    public void ConfigureLogging_IsIdempotent()
    {
        using var a = AngzarrServer.ConfigureLogging();
        using var b = AngzarrServer.ConfigureLogging();
        // Two factories are independent objects (each owns its providers),
        // but the call shouldn't throw on the second invocation.
        a.Should().NotBeNull();
        b.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "angzarr-test-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
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

// xUnit collection so env-mutating tests don't interleave with each other
// or with the ReadinessTests (which also mutates env).
[CollectionDefinition(nameof(EnvCollection), DisableParallelization = true)]
public sealed class EnvCollection { }
