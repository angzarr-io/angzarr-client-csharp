using Google.Protobuf.WellKnownTypes;

namespace Angzarr.Client;

/// <summary>
/// Fluent builder for constructing and executing event queries.
///
/// <para>QueryBuilder supports multiple access patterns:</para>
/// <list type="bullet">
///   <item>By root - fetch all events for a specific aggregate</item>
///   <item>By correlation ID - fetch events across aggregates in a workflow</item>
///   <item>By sequence range - fetch specific event windows for pagination</item>
///   <item>By temporal point - reconstruct historical state (as-of queries)</item>
///   <item>By edition - query from specific schema versions</item>
/// </list>
///
/// <example>
/// <code>
/// var events = client.Query("orders", orderId)
///     .Range(10)
///     .GetEventBook();
///
/// // Or temporal query
/// var historical = client.Query("orders", orderId)
///     .AsOfSequence(42)
///     .GetEventBook();
/// </code>
/// </example>
/// </summary>
public class QueryBuilder
{
    private readonly QueryClient _client;
    private readonly string _domain;
    private Guid? _root;
    private string? _correlationId;
    // Audit finding #23: a single selection slot — matches Rust's
    // `selection: Option<Selection>` so chained range/as_of_sequence/as_of_time
    // setters are last-wins instead of leaving stale state behind.
    private Angzarr.SequenceRange? _rangeSelect;
    private Angzarr.TemporalQuery? _temporal;
    private string? _edition;

    /// <summary>
    /// Create a query builder for a specific aggregate.
    ///
    /// <para>Spec MED-3.10: empty-domain construction is permitted (matches
    /// Py/Rs/Go/Ja/Cpp). Previously this ctor threw; the validation moved
    /// to call sites that care, so cross-language test scenarios produce
    /// equivalent shapes.</para>
    /// </summary>
    /// <param name="client">The query client to use</param>
    /// <param name="domain">The aggregate domain</param>
    /// <param name="root">The aggregate root GUID</param>
    public QueryBuilder(QueryClient client, string domain, Guid root)
    {
        _client = client;
        _domain = domain;
        _root = root;
    }

    /// <summary>
    /// Create a query builder by domain only (use with ByCorrelationId).
    /// Spec MED-3.10 — see ctor above.
    /// </summary>
    /// <param name="client">The query client to use</param>
    /// <param name="domain">The aggregate domain</param>
    public QueryBuilder(QueryClient client, string domain)
    {
        _client = client;
        _domain = domain;
        _root = null;
    }

    /// <summary>
    /// Query by correlation ID instead of root.
    ///
    /// <para>Correlation IDs link events across aggregates in a distributed workflow.</para>
    /// </summary>
    /// <param name="id">The correlation ID</param>
    /// <returns>This builder for chaining</returns>
    public QueryBuilder ByCorrelationId(string id)
    {
        _correlationId = id;
        _root = null;
        return this;
    }

    /// <summary>
    /// Query events from a specific edition.
    ///
    /// <para>After upcasting (event schema migration), events exist in multiple editions.</para>
    /// </summary>
    /// <param name="edition">The edition name</param>
    /// <returns>This builder for chaining</returns>
    public QueryBuilder WithEdition(string edition)
    {
        _edition = edition;
        return this;
    }

    /// <summary>
    /// Query a range of sequences from lower (inclusive).
    ///
    /// <para>Use for incremental sync: "give me events since sequence N".</para>
    ///
    /// <para>Last-selection-wins (audit #23): clears any previously-set temporal
    /// selection so chained calls like
    /// <c>.AsOfSequence(10).Range(5)</c> produce a Query with only the range.</para>
    /// </summary>
    /// <param name="lower">The lower bound (inclusive)</param>
    /// <returns>This builder for chaining</returns>
    public QueryBuilder Range(int lower)
    {
        _rangeSelect = new Angzarr.SequenceRange { Lower = (uint)lower };
        _temporal = null;
        return this;
    }

    /// <summary>
    /// Query a range of sequences with upper bound (inclusive — audit #27).
    ///
    /// <para>Use for pagination: fetch events 100-200, then 200-300.</para>
    ///
    /// <para>Last-selection-wins (audit #23) — see <see cref="Range"/>.</para>
    /// </summary>
    /// <param name="lower">The lower bound (inclusive)</param>
    /// <param name="upper">The upper bound (inclusive)</param>
    /// <returns>This builder for chaining</returns>
    public QueryBuilder RangeTo(int lower, int upper)
    {
        _rangeSelect = new Angzarr.SequenceRange { Lower = (uint)lower, Upper = (uint)upper };
        _temporal = null;
        return this;
    }

    /// <summary>
    /// Query state as of a specific sequence number.
    ///
    /// <para>Essential for debugging: "What was the state when this bug occurred?"</para>
    ///
    /// <para>Last-selection-wins (audit #23) — clears any previously-set range
    /// selection.</para>
    /// </summary>
    /// <param name="seq">The sequence number</param>
    /// <returns>This builder for chaining</returns>
    public QueryBuilder AsOfSequence(int seq)
    {
        _temporal = new Angzarr.TemporalQuery { AsOfSequence = (uint)seq };
        _rangeSelect = null;
        return this;
    }

    /// <summary>
    /// Query state as of a specific timestamp (RFC3339 format).
    ///
    /// <para>Example: <c>"2024-01-15T10:30:00Z"</c>.</para>
    ///
    /// <para>Audit finding #34 (Option B — raise immediately): a malformed
    /// <paramref name="rfc3339"/> string raises <see cref="InvalidTimestampError"/>
    /// synchronously rather than deferring to <see cref="Build"/>. Previously
    /// the failure was captured into a sticky <c>_error</c> field that survived
    /// subsequent last-call-wins setters, making
    /// <c>qb.AsOfTime("bad").AsOfSequence(5).Build()</c> raise the stale parse
    /// error. Mirrors Rust's <c>as_of_time(...) -&gt; Result&lt;Self&gt;</c>
    /// signature where the bad call short-circuits at the call site.</para>
    ///
    /// <para>Last-selection-wins (audit #23) — clears any previously-set range
    /// selection.</para>
    /// </summary>
    /// <param name="rfc3339">The timestamp in RFC3339 format</param>
    /// <returns>This builder for chaining</returns>
    /// <exception cref="InvalidTimestampError">If the timestamp cannot be parsed.</exception>
    public QueryBuilder AsOfTime(string rfc3339)
    {
        var ts = Helpers.ParseTimestamp(rfc3339);
        _temporal = new Angzarr.TemporalQuery { AsOfTime = ts };
        _rangeSelect = null;
        return this;
    }

    /// <summary>
    /// Build the Query without executing.
    /// </summary>
    /// <returns>The constructed Query</returns>
    public Angzarr.Query Build()
    {
        var cover = new Angzarr.Cover { Domain = _domain };

        if (!string.IsNullOrEmpty(_correlationId))
            cover.CorrelationId = _correlationId;

        if (_root.HasValue)
            cover.Root = Helpers.UuidToProto(_root.Value);

        if (!string.IsNullOrEmpty(_edition))
            cover.Edition = new Angzarr.Edition { Name = _edition };

        var query = new Angzarr.Query { Cover = cover };

        if (_rangeSelect != null)
            query.Range = _rangeSelect;
        else if (_temporal != null)
            query.Temporal = _temporal;

        return query;
    }

    /// <summary>
    /// Execute the query and return a single EventBook.
    /// </summary>
    /// <returns>The EventBook containing matching events</returns>
    /// <exception cref="GrpcError">If the gRPC call fails</exception>
    public Angzarr.EventBook GetEventBook()
    {
        var query = Build();
        return _client.GetEventBook(query);
    }

    /// <summary>
    /// Execute the query and return all matching EventBooks.
    /// </summary>
    /// <returns>List of EventBooks</returns>
    /// <exception cref="GrpcError">If the gRPC call fails</exception>
    public List<Angzarr.EventBook> GetEvents()
    {
        var query = Build();
        return _client.GetEvents(query);
    }

    /// <summary>
    /// Execute the query and return just the event pages.
    ///
    /// <para>Convenience method when you only need events, not metadata.</para>
    /// </summary>
    /// <returns>List of EventPages</returns>
    /// <exception cref="GrpcError">If the gRPC call fails</exception>
    public IList<Angzarr.EventPage> GetPages()
    {
        var book = GetEventBook();
        return book.Pages;
    }
}
