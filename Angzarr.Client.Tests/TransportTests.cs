using FluentAssertions;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for the transport-mode resolver — mirrors Rust's <c>transport.rs</c>
/// and Python's <c>resolve_ch_endpoint</c>.
///
/// <para>Audit finding #40: env-var bad-input policy is loud-fail. An unset
/// variable falls through to its default; a SET-but-unrecognized value
/// throws a structural error so operator typos
/// (<c>ANGZARR_MODE=Distrib</c>, <c>ANGZARR_CH_PORT="1310 "</c>) surface at
/// startup instead of silently misrouting.</para>
///
/// <para>Tests serialize on a process-wide lock because they manipulate env vars.</para>
/// </summary>
[Collection("env")]
public class TransportTests
{
    private static readonly object EnvLock = new();

    private static void ClearEnv()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, null);
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, null);
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, null);
        Environment.SetEnvironmentVariable(Transport.EnvChPort, null);
    }

    [Fact]
    public void Standalone_UdsBaseDefaults_WhenNoEnvNoArg()
    {
        lock (EnvLock)
        {
            ClearEnv();
            var ep = Transport.ResolveCheEndpoint("player", TransportMode.Standalone);
            ep.Should().Be("/tmp/angzarr/ch-player.sock");
        }
    }

    [Fact]
    public void Standalone_ArgBeatsDefault_WhenNoEnv()
    {
        lock (EnvLock)
        {
            ClearEnv();
            var ep = Transport.ResolveCheEndpoint(
                "player",
                TransportMode.Standalone,
                udsBase: "/srv/sockets"
            );
            ep.Should().Be("/srv/sockets/ch-player.sock");
        }
    }

    [Fact]
    public void Standalone_EnvBeatsArg()
    {
        lock (EnvLock)
        {
            ClearEnv();
            try
            {
                Environment.SetEnvironmentVariable(Transport.EnvUdsBase, "/env/base");
                var ep = Transport.ResolveCheEndpoint(
                    "player",
                    TransportMode.Standalone,
                    udsBase: "/arg/base"
                );
                ep.Should().Be("/env/base/ch-player.sock");
            }
            finally
            {
                ClearEnv();
            }
        }
    }

    [Fact]
    public void Distributed_NsAndPortDefaults()
    {
        lock (EnvLock)
        {
            ClearEnv();
            var ep = Transport.ResolveCheEndpoint("player", TransportMode.Distributed);
            ep.Should().Be("ch-player.angzarr.svc:1310");
        }
    }

    [Fact]
    public void Distributed_ArgsBeatDefaults()
    {
        lock (EnvLock)
        {
            ClearEnv();
            var ep = Transport.ResolveCheEndpoint(
                "player",
                TransportMode.Distributed,
                @namespace: "my-ns",
                port: 2222
            );
            ep.Should().Be("ch-player.my-ns.svc:2222");
        }
    }

    [Fact]
    public void Distributed_EnvBeatsArgs()
    {
        lock (EnvLock)
        {
            ClearEnv();
            try
            {
                Environment.SetEnvironmentVariable(Transport.EnvNamespace, "env-ns");
                Environment.SetEnvironmentVariable(Transport.EnvChPort, "5555");
                var ep = Transport.ResolveCheEndpoint(
                    "player",
                    TransportMode.Distributed,
                    @namespace: "arg-ns",
                    port: 2222
                );
                ep.Should().Be("ch-player.env-ns.svc:5555");
            }
            finally
            {
                ClearEnv();
            }
        }
    }

    // -------- Audit #40: loud-fail on bad env-var input -------------

    [Fact]
    public void FromEnv_UnsetReturnsDefault()
    {
        lock (EnvLock)
        {
            ClearEnv();
            Transport.TransportModeFromEnv().Should().Be(Transport.DefaultTransportMode);
        }
    }

    [Fact]
    public void FromEnv_UnrecognizedModeThrows()
    {
        lock (EnvLock)
        {
            ClearEnv();
            try
            {
                Environment.SetEnvironmentVariable(Transport.EnvMode, "Distrib");
                Action act = () => Transport.TransportModeFromEnv();
                var ex = act.Should().Throw<InvalidArgumentError>().Which;
                ex.Code.Should().Be(ErrorCodes.InvalidTransportMode);
            }
            finally
            {
                ClearEnv();
            }
        }
    }

    [Fact]
    public void Resolve_UnrecognizedModePropagatesError()
    {
        lock (EnvLock)
        {
            ClearEnv();
            try
            {
                Environment.SetEnvironmentVariable(Transport.EnvMode, "garbage");
                Action act = () => Transport.ResolveCheEndpoint("player");
                var ex = act.Should().Throw<InvalidArgumentError>().Which;
                ex.Code.Should().Be(ErrorCodes.InvalidTransportMode);
            }
            finally
            {
                ClearEnv();
            }
        }
    }

    [Fact]
    public void Resolve_NonNumericPortThrows()
    {
        lock (EnvLock)
        {
            ClearEnv();
            try
            {
                // trailing space — operator typo. Mirrors Rust's
                // resolve_non_numeric_port_errors test.
                Environment.SetEnvironmentVariable(Transport.EnvChPort, "1310 ");
                Action act = () =>
                    Transport.ResolveCheEndpoint("player", TransportMode.Distributed);
                var ex = act.Should().Throw<InvalidArgumentError>().Which;
                ex.Code.Should().Be(ErrorCodes.InvalidPort);
            }
            finally
            {
                ClearEnv();
            }
        }
    }

    // -------- Audit #39: lenient UDS prefix detection ----------------

    [Theory]
    [InlineData("/abs/path", "/abs/path")]
    [InlineData("./rel/path", "./rel/path")]
    [InlineData("unix:relative/path", "relative/path")]
    [InlineData("unix:/abs/path", "/abs/path")]
    [InlineData("unix:///abs/path", "/abs/path")]
    public void DetectUdsPath_RecognizesAllForms(string endpoint, string expected)
    {
        Transport.DetectUdsPath(endpoint).Should().Be(expected);
    }

    [Theory]
    [InlineData("localhost:1310")]
    [InlineData("ch-player.angzarr.svc:1310")]
    [InlineData("http://localhost:1310")]
    [InlineData("https://api.example.com:443")]
    public void DetectUdsPath_ReturnsNullForTcp(string endpoint)
    {
        Transport.DetectUdsPath(endpoint).Should().BeNull();
    }
}
