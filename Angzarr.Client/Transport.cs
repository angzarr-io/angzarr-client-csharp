namespace Angzarr.Client;

/// <summary>
/// Transport mode for gRPC connections.
/// </summary>
public enum TransportMode
{
    /// <summary>Unix Domain Sockets for local-process communication.</summary>
    Standalone,

    /// <summary>TCP via Kubernetes DNS for cluster communication.</summary>
    Distributed,
}

/// <summary>
/// Transport-mode resolver for command handler coordinator endpoints.
///
/// <para>Mirrors the Python (<c>resolve_ch_endpoint</c>) and Rust
/// (<c>transport.rs</c>) implementations so the same env vars resolve to
/// the same endpoints across languages.</para>
///
/// <para>Audit finding #40: env-var bad-input policy is loud-fail. An unset
/// variable falls through to its default; a SET-but-unrecognized value
/// throws an <see cref="InvalidArgumentError"/> so operator typos
/// (<c>ANGZARR_MODE=Distrib</c>, <c>ANGZARR_CH_PORT="1310 "</c>) surface at
/// startup instead of silently misrouting.</para>
/// </summary>
public static class Transport
{
    public const string DefaultUdsBase = "/tmp/angzarr";
    public const string DefaultNamespace = "angzarr";
    public const ushort DefaultChPort = 1310;
    public const TransportMode DefaultTransportMode = TransportMode.Distributed;

    public const string EnvMode = "ANGZARR_MODE";
    public const string EnvUdsBase = "ANGZARR_UDS_BASE";
    public const string EnvNamespace = "ANGZARR_NAMESPACE";
    public const string EnvChPort = "ANGZARR_CH_PORT";

    /// <summary>
    /// Resolve mode from the <c>ANGZARR_MODE</c> env var.
    ///
    /// <list type="bullet">
    ///   <item>Unset → <see cref="DefaultTransportMode"/>.</item>
    ///   <item><c>"standalone"</c> / <c>"distributed"</c> → corresponding variant.</item>
    ///   <item>Anything else → <see cref="InvalidArgumentError"/> with code
    ///   <see cref="ErrorCodes.InvalidTransportMode"/>.</item>
    /// </list>
    ///
    /// <para>Audit finding #40: loud-fail on operator typos.</para>
    /// </summary>
    public static TransportMode TransportModeFromEnv()
    {
        var s = Environment.GetEnvironmentVariable(EnvMode);
        if (string.IsNullOrEmpty(s))
            return DefaultTransportMode;
        return s switch
        {
            "standalone" => TransportMode.Standalone,
            "distributed" => TransportMode.Distributed,
            _ => throw new InvalidArgumentError(
                ErrorMessages.InvalidTransportMode,
                ErrorCodes.InvalidTransportMode,
                new Dictionary<string, string>
                {
                    [ErrorKeys.Input] = s,
                    [ErrorKeys.EnvVar] = EnvMode,
                }
            ),
        };
    }

    /// <summary>
    /// Resolve a domain name to a command handler coordinator endpoint.
    ///
    /// <list type="bullet">
    ///   <item><see cref="TransportMode.Standalone"/> → <c>{udsBase}/ch-{domain}.sock</c></item>
    ///   <item><see cref="TransportMode.Distributed"/> → <c>ch-{domain}.{ns}.svc:{port}</c></item>
    /// </list>
    ///
    /// <para>Resolution precedence per value matches Python: env var &gt;
    /// explicit arg &gt; hardcoded default.</para>
    ///
    /// <para>When <paramref name="mode"/> is <c>null</c>, auto-detects from
    /// <c>ANGZARR_MODE</c>.</para>
    /// </summary>
    /// <summary>
    /// Spec MED-6.8: canonical cross-language name (matches Go's
    /// <c>ResolveCHEndpoint</c>; Rust's <c>resolve_ch_endpoint</c>). The
    /// older <see cref="ResolveCheEndpoint(string,TransportMode?,string?,string?,ushort?)"/>
    /// remains as a deprecated alias.
    /// </summary>
    public static string ResolveCHEndpoint(
        string domain,
        TransportMode? mode = null,
        string? udsBase = null,
        string? @namespace = null,
        ushort? port = null) =>
        ResolveCheEndpointImpl(domain, mode, udsBase, @namespace, port);

    [Obsolete("Use ResolveCHEndpoint (canonical cross-language name per spec MED-6.8). This alias will be removed in a future release.")]
    public static string ResolveCheEndpoint(
        string domain,
        TransportMode? mode = null,
        string? udsBase = null,
        string? @namespace = null,
        ushort? port = null) =>
        ResolveCheEndpointImpl(domain, mode, udsBase, @namespace, port);

    private static string ResolveCheEndpointImpl(
        string domain,
        TransportMode? mode,
        string? udsBase,
        string? @namespace,
        ushort? port
    )
    {
        var resolvedMode = mode ?? TransportModeFromEnv();

        switch (resolvedMode)
        {
            case TransportMode.Standalone:
                {
                    var envBase = Environment.GetEnvironmentVariable(EnvUdsBase);
                    var baseDir = !string.IsNullOrEmpty(envBase)
                        ? envBase
                        : (udsBase ?? DefaultUdsBase);
                    return $"{baseDir}/ch-{domain}.sock";
                }
            case TransportMode.Distributed:
                {
                    var envNs = Environment.GetEnvironmentVariable(EnvNamespace);
                    var ns = !string.IsNullOrEmpty(envNs)
                        ? envNs
                        : (@namespace ?? DefaultNamespace);
                    var envPort = Environment.GetEnvironmentVariable(EnvChPort);
                    ushort p;
                    if (!string.IsNullOrEmpty(envPort))
                    {
                        // Audit #40: loud-fail on non-numeric ANGZARR_CH_PORT.
                        // NumberStyles.None forbids leading/trailing whitespace and
                        // signs; matches Rust's strict u16::parse() and Python's
                        // int(...) which also reject "1310 ".
                        if (!ushort.TryParse(
                                envPort,
                                System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out p
                            ))
                            throw new InvalidArgumentError(
                                ErrorMessages.InvalidPort,
                                ErrorCodes.InvalidPort,
                                new Dictionary<string, string>
                                {
                                    [ErrorKeys.Input] = envPort,
                                    [ErrorKeys.EnvVar] = EnvChPort,
                                }
                            );
                    }
                    else
                    {
                        p = port ?? DefaultChPort;
                    }
                    return $"ch-{domain}.{ns}.svc:{p}";
                }
            default:
                throw new InvalidOperationException($"Unknown TransportMode: {resolvedMode}");
        }
    }

    /// <summary>
    /// Detect a UDS endpoint and return the socket path, or <c>null</c> for TCP.
    ///
    /// <para>Audit finding #39: lenient prefix detection matching Python's
    /// <c>client.py::_create_channel</c>. Recognized forms:</para>
    /// <list type="bullet">
    ///   <item><c>/abs/path</c> — absolute path, no scheme</item>
    ///   <item><c>./rel/path</c> — relative path, no scheme</item>
    ///   <item><c>unix:relative/path</c> — gRPC URI, relative</item>
    ///   <item><c>unix:/abs/path</c> — gRPC URI, absolute (single-slash form)</item>
    ///   <item><c>unix:///abs/path</c> — gRPC URI, absolute with empty authority</item>
    /// </list>
    /// <para>Anything else is treated as TCP.</para>
    /// </summary>
    public static string? DetectUdsPath(string endpoint)
    {
        if (endpoint.StartsWith("unix:///"))
            return endpoint.Substring("unix://".Length);
        if (endpoint.StartsWith("unix://"))
            return endpoint.Substring("unix://".Length);
        if (endpoint.StartsWith("unix:"))
            return endpoint.Substring("unix:".Length);
        if (endpoint.StartsWith("/") || endpoint.StartsWith("./"))
            return endpoint;
        return null;
    }
}
