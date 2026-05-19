// Mirrors Python's tests/test_transport_loudfail.py and Rust's
// src/transport.rs #[cfg(test)] block — pin the structured-details map
// keys, the canonical env-var names, and the static error messages used
// when ANGZARR_MODE / ANGZARR_CH_PORT receive invalid input.
//
// The existing TransportTests.cs covers happy-path and basic loud-fail
// shape; this file pins the assertions that kill error-envelope mutants:
// detail values are forwarded verbatim, env-var names are spelled exactly,
// and bad-mode error messages come from the canonical ErrorMessages
// inventory.

using System;
using System.Collections.Generic;
using Angzarr.Client;
using Xunit;

namespace Angzarr.Client.Tests;

// xUnit doesn't have process-level env-var fixturing by default; these
// tests scrub the four ANGZARR_* env vars before each call and restore
// after. They run serially (no [Collection]) but each method clears its
// own subset to avoid cross-pollution.
public class TransportEnvVarConstantsMirrorTests
{
    // Mirrors test_env_mode_constant_value.
    [Fact]
    public void EnvMode_IsExactCanonicalString()
    {
        Assert.Equal("ANGZARR_MODE", Transport.EnvMode);
    }

    // Mirrors test_env_uds_base_constant_value.
    [Fact]
    public void EnvUdsBase_IsExactCanonicalString()
    {
        Assert.Equal("ANGZARR_UDS_BASE", Transport.EnvUdsBase);
    }

    // Mirrors test_env_namespace_constant_value.
    [Fact]
    public void EnvNamespace_IsExactCanonicalString()
    {
        Assert.Equal("ANGZARR_NAMESPACE", Transport.EnvNamespace);
    }

    // Mirrors test_env_ch_port_constant_value.
    [Fact]
    public void EnvChPort_IsExactCanonicalString()
    {
        Assert.Equal("ANGZARR_CH_PORT", Transport.EnvChPort);
    }
}

[Collection("TransportEnvSerial")]
public class TransportBadModeMirrorTests : IDisposable
{
    private readonly string? _origMode;
    private readonly string? _origUds;
    private readonly string? _origNs;
    private readonly string? _origPort;

    public TransportBadModeMirrorTests()
    {
        _origMode = Environment.GetEnvironmentVariable(Transport.EnvMode);
        _origUds = Environment.GetEnvironmentVariable(Transport.EnvUdsBase);
        _origNs = Environment.GetEnvironmentVariable(Transport.EnvNamespace);
        _origPort = Environment.GetEnvironmentVariable(Transport.EnvChPort);
        Environment.SetEnvironmentVariable(Transport.EnvMode, null);
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, null);
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, null);
        Environment.SetEnvironmentVariable(Transport.EnvChPort, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, _origMode);
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, _origUds);
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, _origNs);
        Environment.SetEnvironmentVariable(Transport.EnvChPort, _origPort);
    }

    // Mirrors test_bad_transport_mode_env_raises_invalid_argument_error.
    [Fact]
    public void BadTransportModeEnv_ThrowsInvalidArgumentError()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "Distrib");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.TransportModeFromEnv());
        Assert.Equal(ErrorCodes.InvalidTransportMode, err.Code);
    }

    // Mirrors test_bad_transport_mode_env_includes_input_in_details.
    [Fact]
    public void BadTransportModeEnv_DetailsIncludeInputVerbatim()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "Distrib");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.TransportModeFromEnv());
        Assert.True(err.Details.ContainsKey(ErrorKeys.Input));
        Assert.Equal("Distrib", err.Details[ErrorKeys.Input]);
    }

    // Mirrors test_bad_transport_mode_env_includes_env_var_in_details.
    [Fact]
    public void BadTransportModeEnv_DetailsIncludeEnvVarName()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "Distrib");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.TransportModeFromEnv());
        Assert.True(err.Details.ContainsKey(ErrorKeys.EnvVar));
        Assert.Equal("ANGZARR_MODE", err.Details[ErrorKeys.EnvVar]);
    }

    // Mirrors test_bad_transport_mode_predicate_is_invalid_argument.
    [Fact]
    public void BadTransportModeEnv_PredicateIsInvalidArgument()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "Distrib");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.TransportModeFromEnv());
        Assert.True(err.IsInvalidArgument());
    }

    // Mirrors test_bad_transport_mode_error_carries_static_message.
    [Fact]
    public void BadTransportModeEnv_MessageIsCanonicalStaticInventoryString()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "garbage");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.TransportModeFromEnv());
        Assert.Equal(ErrorMessages.InvalidTransportMode, err.Message);
    }
}

[Collection("TransportEnvSerial")]
public class TransportBadPortMirrorTests : IDisposable
{
    private readonly string? _origMode;
    private readonly string? _origUds;
    private readonly string? _origNs;
    private readonly string? _origPort;

    public TransportBadPortMirrorTests()
    {
        _origMode = Environment.GetEnvironmentVariable(Transport.EnvMode);
        _origUds = Environment.GetEnvironmentVariable(Transport.EnvUdsBase);
        _origNs = Environment.GetEnvironmentVariable(Transport.EnvNamespace);
        _origPort = Environment.GetEnvironmentVariable(Transport.EnvChPort);
        Environment.SetEnvironmentVariable(Transport.EnvMode, null);
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, null);
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, null);
        Environment.SetEnvironmentVariable(Transport.EnvChPort, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, _origMode);
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, _origUds);
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, _origNs);
        Environment.SetEnvironmentVariable(Transport.EnvChPort, _origPort);
    }

    // Mirrors test_bad_ch_port_env_raises_invalid_argument_error.
    [Fact]
    public void BadChPortEnv_ThrowsInvalidArgumentError()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        Environment.SetEnvironmentVariable(Transport.EnvChPort, "notanumber");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.ResolveCHEndpoint("player"));
        Assert.Equal(ErrorCodes.InvalidPort, err.Code);
    }

    // Mirrors test_bad_ch_port_env_includes_input_in_details.
    [Fact]
    public void BadChPortEnv_DetailsIncludeInputVerbatim()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        Environment.SetEnvironmentVariable(Transport.EnvChPort, "1310 ");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.ResolveCHEndpoint("player"));
        Assert.True(err.Details.ContainsKey(ErrorKeys.Input));
        Assert.Equal("1310 ", err.Details[ErrorKeys.Input]);
    }

    // Mirrors test_bad_ch_port_env_includes_env_var_in_details.
    [Fact]
    public void BadChPortEnv_DetailsIncludeEnvVarName()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        Environment.SetEnvironmentVariable(Transport.EnvChPort, "abc");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.ResolveCHEndpoint("player"));
        Assert.True(err.Details.ContainsKey(ErrorKeys.EnvVar));
        Assert.Equal("ANGZARR_CH_PORT", err.Details[ErrorKeys.EnvVar]);
    }

    // Mirrors test_bad_ch_port_error_carries_static_message.
    [Fact]
    public void BadChPortEnv_MessageIsCanonicalStaticInventoryString()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        Environment.SetEnvironmentVariable(Transport.EnvChPort, "garbage");
        var err = Assert.Throws<InvalidArgumentError>(() => Transport.ResolveCHEndpoint("player"));
        Assert.Equal(ErrorMessages.InvalidPort, err.Message);
    }
}

[Collection("TransportEnvSerial")]
public class TransportEndpointFormatMirrorTests : IDisposable
{
    private readonly string? _origMode;
    private readonly string? _origUds;
    private readonly string? _origNs;
    private readonly string? _origPort;

    public TransportEndpointFormatMirrorTests()
    {
        _origMode = Environment.GetEnvironmentVariable(Transport.EnvMode);
        _origUds = Environment.GetEnvironmentVariable(Transport.EnvUdsBase);
        _origNs = Environment.GetEnvironmentVariable(Transport.EnvNamespace);
        _origPort = Environment.GetEnvironmentVariable(Transport.EnvChPort);
        Environment.SetEnvironmentVariable(Transport.EnvMode, null);
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, null);
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, null);
        Environment.SetEnvironmentVariable(Transport.EnvChPort, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, _origMode);
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, _origUds);
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, _origNs);
        Environment.SetEnvironmentVariable(Transport.EnvChPort, _origPort);
    }

    // Mirrors test_standalone_endpoint_format_includes_domain_with_ch_prefix.
    [Fact]
    public void StandaloneEndpointFormat_IsBaseSlashChDomainDotSock()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "standalone");
        var endpoint = Transport.ResolveCHEndpoint("player");
        Assert.Equal("/tmp/angzarr/ch-player.sock", endpoint);
    }

    // Mirrors test_distributed_endpoint_format_includes_domain_namespace_port.
    [Fact]
    public void DistributedEndpointFormat_IsChDomainDotNsDotSvcColonPort()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        var endpoint = Transport.ResolveCHEndpoint("player");
        Assert.Equal("ch-player.angzarr.svc:1310", endpoint);
    }

    // Mirrors test_distributed_endpoint_explicit_args_used_when_no_env.
    [Fact]
    public void DistributedEndpoint_ExplicitArgsUsedWhenNoEnv()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        var endpoint = Transport.ResolveCHEndpoint("player", @namespace: "ns2", port: 9999);
        Assert.Equal("ch-player.ns2.svc:9999", endpoint);
    }

    // Mirrors test_env_overrides_explicit_kwarg_for_namespace.
    [Fact]
    public void DistributedEndpoint_EnvOverridesExplicitNamespace()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        Environment.SetEnvironmentVariable(Transport.EnvNamespace, "fromenv");
        var endpoint = Transport.ResolveCHEndpoint("player", @namespace: "fromarg");
        Assert.Equal("ch-player.fromenv.svc:1310", endpoint);
    }

    // Mirrors test_env_overrides_explicit_kwarg_for_uds_base.
    [Fact]
    public void StandaloneEndpoint_EnvOverridesExplicitUdsBase()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "standalone");
        Environment.SetEnvironmentVariable(Transport.EnvUdsBase, "/from/env");
        var endpoint = Transport.ResolveCHEndpoint("player", udsBase: "/from/arg");
        Assert.Equal("/from/env/ch-player.sock", endpoint);
    }

    // Mirrors test_env_overrides_explicit_kwarg_for_ch_port.
    [Fact]
    public void DistributedEndpoint_EnvOverridesExplicitPort()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        Environment.SetEnvironmentVariable(Transport.EnvChPort, "5555");
        var endpoint = Transport.ResolveCHEndpoint("player", port: 9999);
        Assert.Equal("ch-player.angzarr.svc:5555", endpoint);
    }

    // Mirrors test_unset_angzarr_mode_defaults_to_distributed.
    [Fact]
    public void UnsetAngzarrMode_DefaultsToDistributed()
    {
        var endpoint = Transport.ResolveCHEndpoint("player");
        Assert.StartsWith("ch-player.", endpoint);
        Assert.Contains(".svc:", endpoint);
    }

    // Mirrors test_unset_angzarr_namespace_defaults_to_angzarr.
    [Fact]
    public void UnsetAngzarrNamespace_DefaultsToAngzarr()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        var endpoint = Transport.ResolveCHEndpoint("player");
        Assert.Equal("ch-player.angzarr.svc:1310", endpoint);
    }

    // Mirrors test_unset_angzarr_ch_port_defaults_to_1310.
    [Fact]
    public void UnsetAngzarrChPort_DefaultsTo1310()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "distributed");
        var endpoint = Transport.ResolveCHEndpoint("player");
        Assert.EndsWith(":1310", endpoint);
    }

    // Mirrors test_unset_angzarr_uds_base_defaults_to_tmp_angzarr.
    [Fact]
    public void UnsetAngzarrUdsBase_DefaultsToTmpAngzarr()
    {
        Environment.SetEnvironmentVariable(Transport.EnvMode, "standalone");
        var endpoint = Transport.ResolveCHEndpoint("player");
        Assert.StartsWith("/tmp/angzarr/", endpoint);
    }
}
