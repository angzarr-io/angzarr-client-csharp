using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Angzarr.Client;

/// <summary>
/// Fluent builder for constructing and executing commands.
///
/// <para>CommandBuilder reduces boilerplate when creating commands:</para>
/// <list type="bullet">
///   <item>Chain method calls instead of nested object construction</item>
///   <item>Type-safe methods prevent invalid field combinations</item>
///   <item>Auto-generates correlation IDs when not provided</item>
///   <item>Build incrementally, execute when ready</item>
/// </list>
///
/// <example>
/// <code>
/// var response = client.Command("orders", orderId)
///     .WithCorrelationId("corr-123")
///     .WithSequence(5)
///     .WithCommand(typeUrl, createOrderCmd)
///     .Execute();
/// </code>
/// </example>
/// </summary>
public class CommandBuilder
{
    private readonly CommandHandlerClient _client;
    private readonly string _domain;
    private readonly Guid? _root;
    private string? _correlationId;
    private uint _sequence = 0;
    private bool _sequenceSet = false;
    private string? _typeUrl;
    private byte[]? _payload;
    private Angzarr.MergeStrategy _mergeStrategy = Angzarr.MergeStrategy.MergeCommutative;
    private Angzarr.SyncMode? _syncMode;
    private Exception? _error;

    /// <summary>
    /// Create a command builder for an existing aggregate.
    /// </summary>
    /// <param name="client">The command handler client to use</param>
    /// <param name="domain">The domain</param>
    /// <param name="root">The aggregate root GUID</param>
    public CommandBuilder(CommandHandlerClient client, string domain, Guid root)
    {
        _client = client;
        _domain = domain;
        _root = root;
    }

    /// <summary>
    /// Create a command builder for a new aggregate, optionally
    /// auto-generating the root UUID.
    ///
    /// <para>Audit finding #20 / spec HIGH-3.1: aggregate roots are always
    /// client-assigned across the six polyglot clients. Pass
    /// <paramref name="autoGenerateRoot"/> = <c>true</c> to materialize a
    /// fresh UUID v4 here (matching Python's <c>command_new</c>). The prior
    /// rootless <c>(client, domain)</c> ctor was removed per HIGH-3.1
    /// because audit #67 forbids server-bound CommandBooks without a
    /// stamped root.</para>
    /// </summary>
    /// <param name="client">The command handler client to use.</param>
    /// <param name="domain">The domain.</param>
    /// <param name="autoGenerateRoot">If <c>true</c>, materialize a fresh
    /// UUID v4 as the aggregate root.</param>
    public CommandBuilder(CommandHandlerClient client, string domain, bool autoGenerateRoot)
    {
        if (!autoGenerateRoot)
        {
            // Spec HIGH-3.1: rootless paths are forbidden. The only legal
            // shapes are (client, domain, Guid root) or
            // (client, domain, autoGenerateRoot: true).
            throw new InvalidArgumentError(
                "rootless CommandBuilder construction is not permitted (audit #67)",
                ErrorCodes.CommandPayloadMissing,
                new Dictionary<string, string>
                {
                    [ErrorKeys.Field] = "root",
                    [ErrorKeys.Domain] = domain,
                });
        }
        _client = client;
        _domain = domain;
        _root = Guid.NewGuid();
    }

    /// <summary>
    /// Set the correlation ID for request tracing.
    ///
    /// <para>Correlation IDs link related operations across services.
    /// If not set, a GUID will be auto-generated on build.</para>
    /// </summary>
    /// <param name="id">The correlation ID</param>
    /// <returns>This builder for chaining</returns>
    public CommandBuilder WithCorrelationId(string id)
    {
        _correlationId = id;
        return this;
    }

    /// <summary>
    /// Set the expected sequence number for optimistic locking.
    ///
    /// <para>Defaults to 0 for new aggregates.</para>
    ///
    /// <para>Spec LOW-3.14: negative inputs are rejected at the call site
    /// with the canonical <see cref="ErrorCodes.CommandSequenceMissing"/>
    /// code rather than silently casting to a large uint via two's
    /// complement (e.g. <c>-1</c> → <c>4294967295</c> on the wire).</para>
    /// </summary>
    /// <param name="seq">The sequence number (must be non-negative)</param>
    /// <returns>This builder for chaining</returns>
    /// <exception cref="InvalidArgumentError">If <paramref name="seq"/> is negative.</exception>
    public CommandBuilder WithSequence(int seq)
    {
        if (seq < 0)
            throw new InvalidArgumentError(
                ErrorMessages.CommandSequenceMissing,
                ErrorCodes.CommandSequenceMissing,
                new Dictionary<string, string>
                {
                    [ErrorKeys.Field] = "sequence",
                    [ErrorKeys.Domain] = _domain,
                    [ErrorKeys.Input] = seq.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
        _sequence = (uint)seq;
        _sequenceSet = true;
        return this;
    }

    /// <summary>
    /// Canonical uint overload (spec LOW-3.14). The proto field is
    /// <c>uint32</c>; this overload removes the signed/unsigned mismatch.
    /// </summary>
    public CommandBuilder WithSequence(uint seq)
    {
        _sequence = seq;
        _sequenceSet = true;
        return this;
    }

    /// <summary>
    /// Store the sync mode the builder will use when <see cref="Execute()"/>
    /// is invoked. Mirrors Rust's <c>with_sync_mode</c>; spec MED-3.3.
    /// </summary>
    public CommandBuilder WithSyncMode(Angzarr.SyncMode mode)
    {
        _syncMode = mode;
        return this;
    }

    /// <summary>
    /// Set the merge strategy for conflict resolution.
    ///
    /// <para>Defaults to COMMUTATIVE. Use STRICT for strong consistency.</para>
    /// </summary>
    /// <param name="strategy">The merge strategy</param>
    /// <returns>This builder for chaining</returns>
    public CommandBuilder WithMergeStrategy(Angzarr.MergeStrategy strategy)
    {
        _mergeStrategy = strategy;
        return this;
    }

    /// <summary>
    /// Set the command type URL and message.
    /// </summary>
    /// <param name="typeUrl">The fully-qualified type URL (e.g., "type.googleapis.com/orders.CreateOrder")</param>
    /// <param name="message">The protobuf command message</param>
    /// <returns>This builder for chaining</returns>
    public CommandBuilder WithCommand(string typeUrl, IMessage message)
    {
        try
        {
            _typeUrl = typeUrl;
            _payload = message.ToByteArray();
        }
        catch (Exception e)
        {
            _error = new InvalidArgumentError($"Failed to serialize command: {e.Message}");
        }
        return this;
    }

    /// <summary>
    /// Build the CommandBook without executing.
    /// </summary>
    /// <returns>The constructed CommandBook</returns>
    /// <exception cref="InvalidArgumentError">If required fields are missing</exception>
    public Angzarr.CommandBook Build()
    {
        if (_error != null)
            throw _error;

        // Spec HIGH-3.2: every builder validation error stamps a canonical
        // SCREAMING_SNAKE code from ErrorCodes plus structured details so
        // cross-language cucumber/parity assertions can key off the code.
        if (string.IsNullOrEmpty(_typeUrl))
            throw new InvalidArgumentError(
                ErrorMessages.CommandTypeUrlMissing,
                ErrorCodes.CommandTypeUrlMissing,
                new Dictionary<string, string>
                {
                    [ErrorKeys.Field] = "type_url",
                    [ErrorKeys.Domain] = _domain,
                });

        if (_payload == null)
            throw new InvalidArgumentError(
                ErrorMessages.CommandPayloadMissing,
                ErrorCodes.CommandPayloadMissing,
                new Dictionary<string, string>
                {
                    [ErrorKeys.Field] = "payload",
                    [ErrorKeys.Domain] = _domain,
                });

        if (!_sequenceSet)
            throw new InvalidArgumentError(
                ErrorMessages.CommandSequenceMissing,
                ErrorCodes.CommandSequenceMissing,
                new Dictionary<string, string>
                {
                    [ErrorKeys.Field] = "sequence",
                    [ErrorKeys.Domain] = _domain,
                });

        var correlationId = _correlationId;
        if (string.IsNullOrEmpty(correlationId))
            correlationId = Guid.NewGuid().ToString();

        var cover = new Angzarr.Cover { Domain = _domain, CorrelationId = correlationId };

        if (_root.HasValue)
            cover.Root = Helpers.UuidToProto(_root.Value);

        var commandAny = new Any { TypeUrl = _typeUrl, Value = ByteString.CopyFrom(_payload) };

        var page = new Angzarr.CommandPage
        {
            Header = new Angzarr.PageHeader { Sequence = _sequence },
            Command = commandAny,
            MergeStrategy = _mergeStrategy,
        };

        var book = new Angzarr.CommandBook { Cover = cover };
        book.Pages.Add(page);

        return book;
    }

    /// <summary>
    /// Build and execute the command.
    /// </summary>
    /// <returns>The command response</returns>
    /// <exception cref="InvalidArgumentError">If required fields are missing</exception>
    /// <exception cref="GrpcError">If the gRPC call fails</exception>
    public Angzarr.CommandResponse Execute()
    {
        var cmd = Build();
        if (_syncMode.HasValue)
            return _client.Handle(cmd, _syncMode.Value);
        return _client.Handle(cmd);
    }

    /// <summary>
    /// Build and execute with an explicit sync mode (overrides any value
    /// stored via <see cref="WithSyncMode"/>).
    /// </summary>
    public Angzarr.CommandResponse Execute(Angzarr.SyncMode syncMode)
    {
        var cmd = Build();
        return _client.Handle(cmd, syncMode);
    }
}
