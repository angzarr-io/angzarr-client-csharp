using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Angzarr.Client.Server;

/// <summary>
/// Common server utilities for angzarr C# examples.
///
/// <para>C# port of <c>angzarr_client/server.py</c> + Rust
/// <c>angzarr-client-rust/src/server.rs</c>. Wires a
/// <see cref="Grpc.Core.Server"/> with built-in health checking
/// and a readiness supervisor that flips <see cref="HealthStatus.Serving"/>
/// only once every probe passes (audit #68).</para>
///
/// <para>Supports both TCP and Unix Domain Socket (UDS) transports.</para>
///
/// <para>Logging uses <c>Microsoft.Extensions.Logging</c> with the same
/// structured event names + field shapes as Python and Rust
/// (<c>server_started</c>, <c>server_shutdown</c>) — audit #89.</para>
/// </summary>
public static class AngzarrServer
{
    /// <summary>
    /// Build an <see cref="ILoggerFactory"/> wired to the console with
    /// JSON-shaped output, ready for the <c>server_started</c> /
    /// <c>server_shutdown</c> structured log lines.
    ///
    /// <para>Idempotent — callers may invoke this on every server start;
    /// each call returns a fresh, disposable factory.</para>
    /// </summary>
    public static ILoggerFactory ConfigureLogging()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
                {
                    Indented = false,
                };
            });
        });
    }

    /// <summary>
    /// Clean up a UDS socket file. No-op if missing or empty.
    /// Mirrors Python's <c>cleanup_socket</c>.
    /// </summary>
    public static void CleanupSocket(string? socketPath)
    {
        if (string.IsNullOrEmpty(socketPath))
            return;
        try
        {
            if (File.Exists(socketPath))
                File.Delete(socketPath);
        }
        catch (IOException) { /* best-effort, matches Python */ }
        catch (UnauthorizedAccessException) { /* best-effort, matches Python */ }
    }
}

/// <summary>
/// Environment-driven transport configuration.
///
/// <para>Cross-language alias: matches Rust's
/// <c>server::ServerConfig::from_env</c> + Python's
/// <c>server.get_transport_config</c>.</para>
/// </summary>
public sealed class ServerConfig
{
    /// <summary>TCP port (used when <see cref="UdsPath"/> is null).</summary>
    public int Port { get; }

    /// <summary>Unix domain socket path. When non-null, supersedes <see cref="Port"/>.</summary>
    public string? UdsPath { get; }

    public ServerConfig(int port = 50052, string? udsPath = null)
    {
        Port = port;
        UdsPath = udsPath;
    }

    /// <summary>
    /// Resolve from env. UDS mode is selected when all three of
    /// <c>UDS_BASE_PATH</c>, <c>SERVICE_NAME</c>, and <c>DOMAIN</c> are
    /// set; otherwise TCP, with port read from <c>PORT</c> or
    /// <c>GRPC_PORT</c>, falling back to <paramref name="default_port"/>.
    ///
    /// <para>This function is pure — no filesystem side effects. The
    /// runner is responsible for creating the parent directory and
    /// removing any stale socket file at the chosen path (see
    /// <see cref="ServerConfigEnv.GetTransportConfig"/>).</para>
    /// </summary>
    public static ServerConfig FromEnv(int default_port = 50052)
    {
        var basePath = Environment.GetEnvironmentVariable("UDS_BASE_PATH");
        var serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME");
        var domain = Environment.GetEnvironmentVariable("DOMAIN");
        if (!string.IsNullOrEmpty(basePath) && !string.IsNullOrEmpty(serviceName) && !string.IsNullOrEmpty(domain))
        {
            var socketName = $"{serviceName}-{domain}.sock";
            var uds = Path.Combine(basePath, socketName);
            return new ServerConfig(port: default_port, udsPath: uds);
        }

        var portStr = Environment.GetEnvironmentVariable("PORT")
                      ?? Environment.GetEnvironmentVariable("GRPC_PORT");
        var port = default_port;
        if (!string.IsNullOrEmpty(portStr) &&
            int.TryParse(portStr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var p))
        {
            port = p;
        }
        return new ServerConfig(port: port, udsPath: null);
    }
}

/// <summary>
/// Pure-compute env resolvers for the gRPC transport address. Equivalent
/// to Python's <c>server.get_transport_config</c> + <c>resolve_bind_address</c>
/// and Rust's <c>server::resolve_bind_address</c> /
/// <c>get_transport_config</c>.
///
/// <para>Audit #77 — <c>ANGZARR_BIND_ADDRESS</c> overrides the default
/// <c>[::]:{port}</c> composition for TCP. IPv6 hosts must include
/// brackets (e.g. <c>[::1]:50052</c>); IPv4 hosts are written bare.</para>
/// </summary>
public static class ServerConfigEnv
{
    public const string EnvBindAddress = "ANGZARR_BIND_ADDRESS";
    public const string DefaultBindHost = "[::]";
    public const int DefaultPort = 50052;

    /// <summary>
    /// Compute the TCP bind address.
    /// Returns <c>ANGZARR_BIND_ADDRESS</c> verbatim when set, otherwise
    /// composes <c>[::]:{port}</c> where <c>port</c> comes from
    /// <c>PORT</c> env var or <paramref name="default_port"/>.
    /// </summary>
    public static string ResolveBindAddress(int default_port = DefaultPort)
    {
        var overrideAddr = Environment.GetEnvironmentVariable(EnvBindAddress);
        if (!string.IsNullOrEmpty(overrideAddr))
            return overrideAddr;
        var port = Environment.GetEnvironmentVariable("PORT") ?? default_port.ToString();
        return $"{DefaultBindHost}:{port}";
    }

    /// <summary>
    /// Get transport configuration from environment.
    /// <list type="bullet">
    ///   <item>TCP: <c>("tcp", "[::]:{port}")</c> — overridable via <c>ANGZARR_BIND_ADDRESS</c></item>
    ///   <item>UDS: <c>("uds", "unix:{socket_path}")</c></item>
    /// </list>
    /// Has filesystem side effects in UDS mode: creates the parent
    /// directory if missing and removes any stale socket file at the
    /// chosen path (parity with Python).
    /// </summary>
    public static (string Transport, string Address) GetTransportConfig()
    {
        var transport = (Environment.GetEnvironmentVariable("TRANSPORT_TYPE") ?? "tcp").ToLowerInvariant();

        if (transport == "uds")
        {
            var basePath = Environment.GetEnvironmentVariable("UDS_BASE_PATH") ?? "/tmp/angzarr";
            var serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "business";
            var qualifier = Environment.GetEnvironmentVariable("DOMAIN")
                            ?? Environment.GetEnvironmentVariable("SAGA_NAME")
                            ?? Environment.GetEnvironmentVariable("PROJECTOR_NAME")
                            ?? "";

            var socketPath = !string.IsNullOrEmpty(qualifier)
                ? Path.Combine(basePath, $"{serviceName}-{qualifier}.sock")
                : Path.Combine(basePath, $"{serviceName}.sock");

            // Same side effects as Python: ensure parent dir exists, remove
            // stale socket file.
            try
            {
                var parent = Path.GetDirectoryName(socketPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
            }
            catch (IOException) { /* best-effort */ }

            if (File.Exists(socketPath))
            {
                try { File.Delete(socketPath); } catch (IOException) { }
            }

            return ("uds", $"unix:{socketPath}");
        }

        return ("tcp", ResolveBindAddress());
    }
}
