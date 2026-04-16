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
    /// Throws InvalidOperationException if the domain is not in the sequences map.
    /// This indicates a configuration error -- the domain should be listed in output_domains.
    /// </summary>
    /// <param name="cmd">The command book to stamp.</param>
    /// <param name="domain">The domain whose sequence to use.</param>
    /// <returns>The same command book, with pages stamped.</returns>
    public Angzarr.CommandBook StampCommand(Angzarr.CommandBook cmd, string domain)
    {
        if (!_sequences.TryGetValue(domain, out var seq))
            throw new InvalidArgumentError(
                $"No sequence for domain '{domain}' - check output_domains config"
            );

        foreach (var page in cmd.Pages)
        {
            page.Header = new Angzarr.PageHeader { Sequence = seq };
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
    /// Get all domain names that have sequences.
    /// </summary>
    public IReadOnlyCollection<string> Domains => _sequences.Keys;
}
