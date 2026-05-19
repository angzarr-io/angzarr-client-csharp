namespace Angzarr.Client;

/// <summary>
/// Provides access to destination sequences for command stamping.
///
/// Sagas and PMs receive destination sequences from the framework based on
/// output_domains configured in the component config.
///
/// Design Philosophy:
/// - Sagas/PMs are translators, NOT decision makers
/// - They should NOT rebuild destination state to make business decisions
/// - Business logic belongs in aggregates
/// - Destinations provide only sequences for command stamping
///
/// Example:
/// <code>
/// public SagaHandlerResponse Execute(
///     Angzarr.EventBook source,
///     Any eventPayload,
///     Destinations destinations)
/// {
///     var cmd = new CommandBook { ... };
///     destinations.StampCommand(cmd, "fulfillment");
///     return SagaHandlerResponse.WithCommands(new[] { cmd });
/// }
/// </code>
/// </summary>
public class Destinations
{
    private readonly Dictionary<string, uint> _sequences;

    /// <summary>
    /// Create a Destinations from a sequences dictionary.
    /// The sequences map comes from the gRPC request's destination_sequences field.
    /// </summary>
    /// <param name="sequences">Map of domain name to next sequence number.</param>
    public Destinations(IDictionary<string, uint> sequences)
    {
        _sequences = new Dictionary<string, uint>(sequences);
    }

    /// <summary>
    /// Get the next sequence number for a domain.
    /// Returns null if the domain is not in the sequences map.
    /// </summary>
    /// <param name="domain">The domain to look up.</param>
    /// <returns>The sequence number, or null if not found.</returns>
    public uint? SequenceFor(string domain)
    {
        return _sequences.TryGetValue(domain, out var seq) ? seq : null;
    }

    /// <summary>
    /// Stamp all command pages with the sequence for the given domain.
    ///
    /// <para>Throws <see cref="InvalidArgumentError"/> with code
    /// <see cref="ErrorCodes.MissingDestinationSequence"/> when the domain
    /// is not in the sequences map. This indicates a configuration error —
    /// the domain should be listed in <c>output_domains</c>. Audit #64.</para>
    /// </summary>
    /// <param name="cmd">The command book to stamp.</param>
    /// <param name="domain">The domain whose sequence to use.</param>
    /// <returns>The same command book, with pages stamped.</returns>
    public Angzarr.CommandBook StampCommand(Angzarr.CommandBook cmd, string domain)
    {
        if (!_sequences.TryGetValue(domain, out var seq))
            throw new InvalidArgumentError(
                ErrorMessages.MissingDestinationSequence,
                ErrorCodes.MissingDestinationSequence,
                new Dictionary<string, string> { [ErrorKeys.Domain] = domain }
            );

        foreach (var page in cmd.Pages)
        {
            // Mirror Python: preserve existing header fields, just set sequence.
            // (Was previously replacing the entire header — caller could lose
            // an AngzarrDeferred or merge-strategy override they had built.)
            if (page.Header == null)
                page.Header = new Angzarr.PageHeader { Sequence = seq };
            else
                page.Header.Sequence = seq;
        }

        return cmd;
    }

    /// <summary>
    /// Check if a sequence exists for the given domain.
    /// </summary>
    /// <param name="domain">The domain to check.</param>
    /// <returns>True if the domain has a sequence.</returns>
    public bool Has(string domain)
    {
        return _sequences.ContainsKey(domain);
    }

    /// <summary>
    /// Alias for <see cref="Has"/> matching the Python/Rust API name.
    /// Audit #74 — canonicalized cross-language naming.
    /// </summary>
    public bool HasDomain(string domain) => Has(domain);

    /// <summary>
    /// Get all domain names that have sequences.
    /// </summary>
    public IReadOnlyCollection<string> Domains => _sequences.Keys;

    /// <summary>
    /// Build a <see cref="Angzarr.PageHeader"/> carrying an
    /// <see cref="Angzarr.AngzarrDeferredSequence"/>.
    ///
    /// <para>Use this on saga-produced commands so the framework can dedupe on
    /// <c>(source.root, source_seq, target.root)</c>. AMQP at-least-once
    /// redelivery of the trigger event then becomes a no-op at the
    /// destination aggregate's pipeline.</para>
    ///
    /// <para>Mirrors Python's <c>Destinations.deferred_header(source_cover, source_seq)</c>
    /// and Rust's <c>Destinations::deferred_header</c>. Audit #31.</para>
    /// </summary>
    public static Angzarr.PageHeader DeferredHeader(Angzarr.Cover sourceCover, uint sourceSeq)
    {
        return new Angzarr.PageHeader
        {
            AngzarrDeferred = new Angzarr.AngzarrDeferredSequence
            {
                Source = sourceCover,
                SourceSeq = sourceSeq,
            },
        };
    }
}
