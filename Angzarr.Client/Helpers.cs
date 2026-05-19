using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Angzarr.Client;

/// <summary>
/// Helper methods for working with Angzarr types.
/// </summary>
public static class Helpers
{
    // Constants matching Rust proto_ext::constants
    public const string UnknownDomain = "unknown";
    public const string WildcardDomain = "*";

    // Spec HIGH-2.1: canonical DEFAULT_EDITION is the empty string. Py/Rs/Ja/Cpp
    // all use ""; the previous "angzarr" value broke cross-language cache_key
    // byte-equality. Edition objects with an empty Name are the canonical main
    // timeline (audit #50).
    public const string DefaultEdition = "";
    public const string MetaAngzarrDomain = "_angzarr";
    public const string ProjectionDomainPrefix = "_projection";
    public const string CorrelationIdHeader = "x-correlation-id";
    public const string TypeUrlPrefix = "type.googleapis.com/";

    // Spec LOW-2.11: cross-language constant present in Py/Rs/Ja/Cpp.
    public const string ProjectionTypeUrl =
        "type.googleapis.com/angzarr_client.proto.angzarr.Projection";

    // Spec MED-4.5 (referenced by router code in other langs).
    public const string NotificationTypeUrl =
        "type.googleapis.com/angzarr_client.proto.angzarr.Notification";

    /// <summary>
    /// Exact-match check against the cross-language Notification type URL.
    /// Spec MED-4.5: replaces suffix-matching (anti-pattern per audit #25).
    /// </summary>
    public static bool IsNotificationTypeUrl(string typeUrl) =>
        typeUrl == NotificationTypeUrl;

    /// <summary>
    /// Get the fully-qualified protobuf type name from a C# proto message type.
    /// Creates a default instance to access the proto descriptor.
    /// </summary>
    /// <param name="messageType">The C# type implementing IMessage (e.g., typeof(RegisterPlayer))</param>
    /// <returns>Fully qualified proto name (e.g., "examples.player.RegisterPlayer")</returns>
    public static string ProtoFullName(System.Type messageType)
    {
        var instance = (IMessage)System.Activator.CreateInstance(messageType)!;
        return instance.Descriptor.FullName;
    }

    /// <summary>
    /// Convert a System.Guid to an Angzarr UUID proto.
    /// </summary>
    public static Angzarr.UUID UuidToProto(Guid guid)
    {
        return new Angzarr.UUID { Value = ByteString.CopyFrom(guid.ToByteArray()) };
    }

    /// <summary>
    /// Convert an Angzarr UUID proto to a System.Guid.
    /// </summary>
    public static Guid ProtoToUuid(Angzarr.UUID uuid)
    {
        return new Guid(uuid.Value.ToByteArray());
    }

    /// <summary>
    /// Get the domain from an EventBook, or <see cref="UnknownDomain"/> if missing
    /// or empty.
    ///
    /// <para>Audit finding #54: empty-domain covers (partially-constructed Cover
    /// during testing, malformed wire input, etc.) are treated as missing.
    /// Mirrors Python's <c>helpers.py::domain</c> and Rust's <c>CoverExt::domain</c>
    /// which fall back for either <c>cover is None</c> OR
    /// <c>!cover.domain</c>. Postel's Law: normalize ambiguous-shaped data.</para>
    /// </summary>
    public static string Domain(Angzarr.EventBook book)
    {
        var d = book.Cover?.Domain;
        return string.IsNullOrEmpty(d) ? UnknownDomain : d;
    }

    /// <summary>
    /// Get the correlation ID from an EventBook.
    /// </summary>
    public static string CorrelationId(Angzarr.EventBook book)
    {
        return book.Cover?.CorrelationId ?? "";
    }

    /// <summary>
    /// Check if an EventBook has a correlation ID.
    /// </summary>
    public static bool HasCorrelationId(Angzarr.EventBook book)
    {
        return !string.IsNullOrEmpty(book.Cover?.CorrelationId);
    }

    /// <summary>
    /// Get the root UUID from an EventBook.
    /// </summary>
    public static Angzarr.UUID? RootUuid(Angzarr.EventBook book)
    {
        return book.Cover?.Root;
    }

    /// <summary>
    /// Get the root UUID as hex string from an EventBook.
    /// </summary>
    public static string RootIdHex(Angzarr.EventBook book)
    {
        var root = book.Cover?.Root;
        if (root == null)
            return "";
        return Convert.ToHexString(root.Value.ToByteArray()).ToLowerInvariant();
    }

    /// <summary>
    /// Get the edition from an EventBook, or null if not set.
    /// </summary>
    public static Angzarr.Edition? Edition(Angzarr.EventBook book)
    {
        var edition = book.Cover?.Edition;
        if (edition == null || string.IsNullOrEmpty(edition.Name))
            return null;
        return edition;
    }

    /// <summary>
    /// Calculate the next sequence number from an EventBook.
    /// Uses the framework-precomputed next_sequence field rather than counting
    /// pages, because snapshots may cause the EventBook to contain only
    /// post-snapshot events — counting pages would give the wrong sequence.
    /// </summary>
    public static uint NextSequence(Angzarr.EventBook? book)
    {
        if (book == null)
            return 0;
        return book.NextSequence;
    }

    /// <summary>
    /// Get the sequence number from an EventPage's header.
    /// Returns 0 if header or sequence is not set.
    /// </summary>
    public static uint SequenceNum(Angzarr.EventPage page)
    {
        return page.Header?.Sequence ?? 0;
    }

    /// <summary>
    /// Get the sequence number from a CommandPage's header.
    /// Returns 0 if header or sequence is not set.
    /// </summary>
    public static uint SequenceNum(Angzarr.CommandPage page)
    {
        return page.Header?.Sequence ?? 0;
    }

    /// <summary>
    /// Set the sequence number on an EventPage's header.
    /// Creates the header if it doesn't exist.
    /// </summary>
    public static void SetSequence(Angzarr.EventPage page, uint sequence)
    {
        page.Header ??= new Angzarr.PageHeader();
        page.Header.Sequence = sequence;
    }

    /// <summary>
    /// Set the sequence number on a CommandPage's header.
    /// Creates the header if it doesn't exist.
    /// </summary>
    public static void SetSequence(Angzarr.CommandPage page, uint sequence)
    {
        page.Header ??= new Angzarr.PageHeader();
        page.Header.Sequence = sequence;
    }

    /// <summary>
    /// Get the type URL for a protobuf message.
    /// </summary>
    public static string TypeUrl(IMessage message)
    {
        return "type.googleapis.com/" + message.Descriptor.FullName;
    }

    /// <summary>
    /// Extract the type name from a type URL.
    /// </summary>
    public static string TypeNameFromUrl(string typeUrl)
    {
        var idx = typeUrl.LastIndexOf('/');
        return idx >= 0 ? typeUrl[(idx + 1)..] : typeUrl;
    }

    /// <summary>
    /// Check if a type URL matches the given fully qualified type name.
    /// </summary>
    /// <param name="typeUrl">Full type URL (e.g., "type.googleapis.com/examples.CardsDealt")</param>
    /// <param name="typeName">Fully qualified type name (e.g., "examples.CardsDealt")</param>
    /// <returns>true if typeUrl equals TypeUrlPrefix + typeName</returns>
    public static bool TypeUrlMatches(string typeUrl, string typeName)
    {
        return typeUrl == TypeUrlPrefix + typeName;
    }

    /// <summary>
    /// Get the current timestamp as a protobuf Timestamp.
    /// </summary>
    public static Timestamp Now()
    {
        return Timestamp.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Parse a timestamp string to a protobuf Timestamp.
    /// </summary>
    public static Timestamp ParseTimestamp(string value)
    {
        if (DateTime.TryParse(value, out var dt))
        {
            return Timestamp.FromDateTime(dt.ToUniversalTime());
        }
        throw new InvalidTimestampError($"Cannot parse timestamp: {value}");
    }

    /// <summary>
    /// Pack a protobuf message into an Any.
    /// </summary>
    public static Any PackAny(IMessage message)
    {
        return Any.Pack(message, "type.googleapis.com/");
    }

    /// <summary>
    /// Pack an event into an EventPage.
    /// </summary>
    public static Angzarr.EventPage PackEvent(IMessage eventMessage)
    {
        return new Angzarr.EventPage { Event = PackAny(eventMessage) };
    }

    /// <summary>
    /// Pack multiple events into EventPages.
    /// </summary>
    public static IEnumerable<Angzarr.EventPage> PackEvents(params IMessage[] events)
    {
        return events.Select(PackEvent);
    }

    /// <summary>
    /// Create a new EventBook with the given events.
    /// </summary>
    public static Angzarr.EventBook NewEventBook(params IMessage[] events)
    {
        var book = new Angzarr.EventBook();
        book.Pages.AddRange(PackEvents(events));
        return book;
    }

    /// <summary>
    /// Create a new EventBook with multiple events.
    /// </summary>
    public static Angzarr.EventBook NewEventBookMulti(IEnumerable<IMessage> events)
    {
        var book = new Angzarr.EventBook();
        book.Pages.AddRange(events.Select(PackEvent));
        return book;
    }

    /// <summary>
    /// Convert raw bytes to a standard UUID text format.
    ///
    /// <para>16-byte input formats as the canonical 8-4-4-4-12 UUID; other lengths
    /// fall back to dashless hex. Mirrors Python's <c>bytes_to_uuid_text</c>.</para>
    /// </summary>
    public static string BytesToUuidText(byte[] bytes)
    {
        if (bytes.Length == 16)
        {
            // Wire bytes are big-endian; System.Guid uses mixed-endian, so we
            // feed it the swapped form to get the canonical dashed UUID text.
            var copy = new byte[16];
            Buffer.BlockCopy(bytes, 0, copy, 0, 16);
            (copy[0], copy[3]) = (copy[3], copy[0]);
            (copy[1], copy[2]) = (copy[2], copy[1]);
            (copy[4], copy[5]) = (copy[5], copy[4]);
            (copy[6], copy[7]) = (copy[7], copy[6]);
            return new Guid(copy).ToString();
        }
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Convert a proto UUID to its standard text format (dashed 8-4-4-4-12 for
    /// 16-byte values; dashless hex otherwise). Audit finding #48.
    /// </summary>
    public static string ToUuidText(Angzarr.UUID? uuid)
    {
        if (uuid == null)
            return "";
        return BytesToUuidText(uuid.Value.ToByteArray());
    }

    /// <summary>
    /// Return the divergence sequence for a domain on the given edition,
    /// or null if the edition is null or no matching divergence is registered.
    /// Mirrors Python's <c>divergence_for</c>. Audit finding #49.
    /// </summary>
    public static uint? DivergenceFor(Angzarr.Edition? edition, string domainName)
    {
        if (edition == null)
            return null;
        foreach (var d in edition.Divergences)
        {
            if (d.Domain == domainName)
                return d.Sequence;
        }
        return null;
    }

    /// <summary>
    /// Build the composite idempotency key for a saga-produced deferred sequence.
    ///
    /// <para>Format: <c>{source.edition}:{source.domain}:{source.root_hex}:{source_seq}</c>.
    /// Returns <c>null</c> when the deferred sequence has no source cover —
    /// a malformed wire input is a missing-key signal, not an exception.
    /// Audit finding #55.</para>
    /// </summary>
    public static string? IdempotencyKey(Angzarr.AngzarrDeferredSequence? deferred)
    {
        if (deferred?.Source == null)
            return null;
        var source = deferred.Source;
        var editionName = source.Edition?.Name ?? "";
        var rootHex = source.Root != null
            ? Convert.ToHexString(source.Root.Value.ToByteArray()).ToLowerInvariant()
            : "";
        return $"{editionName}:{source.Domain}:{rootHex}:{deferred.SourceSeq}";
    }

    /// <summary>
    /// Compute the cache key for a Cover.
    ///
    /// <para>Format: <c>{edition.name}:{cover.domain}:{root_hex}</c> matching
    /// Py/Rs/Go. When the edition is null/empty (main timeline per
    /// HIGH-2.1), the edition portion is the empty string and the key
    /// starts with a leading colon (e.g. <c>:player:abc</c>).</para>
    ///
    /// <para>Spec MED-2.3.</para>
    /// </summary>
    public static string CacheKey(Angzarr.Cover? cover)
    {
        if (cover == null)
            return $"{DefaultEdition}::";
        var editionName = cover.Edition?.Name ?? DefaultEdition;
        var rootText = ToUuidText(cover.Root);
        return $"{editionName}:{cover.Domain}:{rootText}";
    }

    /// <summary>
    /// Compute the routing key for a Cover: <c>{domain}:{root_uuid}</c>
    /// where the root is rendered as canonical 8-4-4-4-12 UUID text.
    /// Spec MED-2.4.
    /// </summary>
    public static string RoutingKey(Angzarr.Cover? cover)
    {
        if (cover == null)
            return ":";
        var rootText = ToUuidText(cover.Root);
        return $"{cover.Domain}:{rootText}";
    }

    /// <summary>
    /// Build gRPC metadata carrying the correlation id header. An empty
    /// correlation id yields empty metadata (audit #69).
    /// Spec MED-2.10.
    /// </summary>
    public static Metadata CorrelatedMetadata(string correlationId)
    {
        var md = new Metadata();
        if (!string.IsNullOrEmpty(correlationId))
            md.Add(CorrelationIdHeader, correlationId);
        return md;
    }

    /// <summary>
    /// Extract the root UUID from a Cover. Returns null when the cover
    /// has no root.
    /// Spec MED-3.11 (free-function helper parity with Rust).
    /// </summary>
    public static Guid? RootFromCover(Angzarr.Cover? cover)
    {
        if (cover?.Root == null)
            return null;
        return ProtoToUuid(cover.Root);
    }

    /// <summary>
    /// Return the events list from a CommandResponse, or an empty
    /// enumerable when the response has no event book.
    /// Spec MED-3.11.
    /// </summary>
    public static IEnumerable<Angzarr.EventPage> EventsFromResponse(Angzarr.CommandResponse? response)
    {
        if (response?.Events?.Pages == null)
            return Array.Empty<Angzarr.EventPage>();
        return response.Events.Pages;
    }

    /// <summary>
    /// Decode a single event from a page by unpacking its Any payload into
    /// the supplied message type.
    /// Spec MED-3.11.
    /// </summary>
    public static T DecodeEvent<T>(Angzarr.EventPage page)
        where T : IMessage, new()
    {
        if (page.Event == null)
            throw new InvalidArgumentError(
                ErrorMessages.MissingCommandPayload,
                ErrorCodes.AnyDecodeFailed,
                new Dictionary<string, string>
                {
                    [ErrorKeys.Field] = "event",
                });
        try
        {
            return page.Event.Unpack<T>();
        }
        catch (Google.Protobuf.InvalidProtocolBufferException pbe)
        {
            throw new InvalidArgumentError(
                ErrorMessages.AnyDecodeFailed,
                ErrorCodes.AnyDecodeFailed,
                new Dictionary<string, string>
                {
                    [ErrorKeys.TypeUrl] = page.Event.TypeUrl,
                    [ErrorKeys.Cause] = pbe.Message,
                });
        }
    }

    /// <summary>
    /// Build a map from root UUID hex to EventBook for destination lookup.
    ///
    /// <para>Cross-language alias for Python's <c>destination_map</c> / Rust's
    /// <c>proto_ext::destination_map</c>. Used in multi-destination sagas to look
    /// up the correct EventBook by aggregate root when stamping command sequences.
    /// Entries without a root are silently skipped.</para>
    /// </summary>
    public static Dictionary<string, Angzarr.EventBook> DestinationMap(
        IEnumerable<Angzarr.EventBook> destinations
    )
    {
        var result = new Dictionary<string, Angzarr.EventBook>();
        foreach (var dest in destinations)
        {
            if (dest.Cover?.Root == null)
                continue;
            var hex = Convert.ToHexString(dest.Cover.Root.Value.ToByteArray()).ToLowerInvariant();
            result[hex] = dest;
        }
        return result;
    }
}
