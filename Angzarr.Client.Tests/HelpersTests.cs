using Angzarr;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for Helpers — ports the Python/Rust audit-finding parity behaviors.
///
/// Covers:
///   #48 — ToUuidText (dashed for 16 bytes, hex fallback otherwise)
///   #49 — DivergenceFor returns nullable
///   #54 — Domain() falls back to UNKNOWN_DOMAIN for empty cover.domain
///   #55 — IdempotencyKey returns nullable (no panic)
///   destination_map cross-language helper
/// </summary>
public class HelpersTests
{
    // --------------------------------------------------------------------
    // Audit #48 — ToUuidText
    // --------------------------------------------------------------------

    [Fact]
    public void ToUuidText_EmitsDashedForm_For16Bytes()
    {
        // Equivalent to Python's bytes_to_uuid_text(b) for 16-byte input.
        // Mirrors Rust's to_uuid_text_emits_dashed_form_for_16_bytes.
        var bytes = new byte[]
        {
            0x6b, 0xa7, 0xb8, 0x10, 0x9d, 0xad, 0x11, 0xd1,
            0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8,
        };
        var proto = new UUID { Value = ByteString.CopyFrom(bytes) };

        Helpers.ToUuidText(proto).Should().Be("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
    }

    [Fact]
    public void ToUuidText_FallsBackToHex_ForNon16Bytes()
    {
        // Non-canonical inputs return dashless hex.
        var proto = new UUID { Value = ByteString.CopyFrom(new byte[] { 0xAB, 0xCD, 0xEF }) };

        Helpers.ToUuidText(proto).Should().Be("abcdef");
    }

    [Fact]
    public void ToUuidText_Null_ReturnsEmptyString()
    {
        Helpers.ToUuidText(null).Should().Be("");
    }

    // --------------------------------------------------------------------
    // Audit #49 — DivergenceFor returns nullable
    // --------------------------------------------------------------------

    [Fact]
    public void DivergenceFor_NullEdition_ReturnsNull()
    {
        Helpers.DivergenceFor(null, "order").Should().BeNull();
    }

    [Fact]
    public void DivergenceFor_DomainAbsent_ReturnsNull()
    {
        var edition = new Edition { Name = "v2" };
        edition.Divergences.Add(new DomainDivergence { Domain = "player", Sequence = 5 });

        Helpers.DivergenceFor(edition, "order").Should().BeNull();
    }

    [Fact]
    public void DivergenceFor_DomainPresent_ReturnsSequence()
    {
        var edition = new Edition { Name = "v2" };
        edition.Divergences.Add(new DomainDivergence { Domain = "order", Sequence = 42 });

        Helpers.DivergenceFor(edition, "order").Should().Be(42u);
    }

    // --------------------------------------------------------------------
    // Audit #54 — Domain() falls back when cover.domain empty
    // --------------------------------------------------------------------

    [Fact]
    public void Domain_FallsBackToUnknown_WhenCoverMissing()
    {
        var book = new EventBook();

        Helpers.Domain(book).Should().Be(Helpers.UnknownDomain);
    }

    [Fact]
    public void Domain_FallsBackToUnknown_WhenDomainEmpty()
    {
        // Audit #54: empty-domain covers (partially-constructed Cover during
        // testing, malformed wire input, etc.) are treated as missing.
        var book = new EventBook
        {
            Cover = new Cover { Domain = "" },
        };

        Helpers.Domain(book).Should().Be(Helpers.UnknownDomain);
    }

    [Fact]
    public void Domain_ReturnsSetValue()
    {
        var book = new EventBook { Cover = new Cover { Domain = "order" } };

        Helpers.Domain(book).Should().Be("order");
    }

    // --------------------------------------------------------------------
    // Audit #55 — IdempotencyKey returns nullable
    // --------------------------------------------------------------------

    [Fact]
    public void IdempotencyKey_NullDeferred_ReturnsNull()
    {
        Helpers.IdempotencyKey(null).Should().BeNull();
    }

    [Fact]
    public void IdempotencyKey_MissingSource_ReturnsNull()
    {
        // A deferred sequence without source cover ⇒ malformed wire input.
        // Should return null, NOT throw — the missing key is the caller's signal.
        var deferred = new AngzarrDeferredSequence { SourceSeq = 7 };

        Helpers.IdempotencyKey(deferred).Should().BeNull();
    }

    [Fact]
    public void IdempotencyKey_WithSource_ReturnsComposite()
    {
        // Format: "{edition}:{domain}:{root_hex}:{source_seq}"
        var rootBytes = new byte[]
        {
            0x6b, 0xa7, 0xb8, 0x10, 0x9d, 0xad, 0x11, 0xd1,
            0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8,
        };
        var source = new Cover
        {
            Domain = "order",
            Root = new UUID { Value = ByteString.CopyFrom(rootBytes) },
            Edition = new Edition { Name = "v2" },
        };
        var deferred = new AngzarrDeferredSequence { Source = source, SourceSeq = 3 };

        Helpers.IdempotencyKey(deferred).Should().Be("v2:order:6ba7b8109dad11d180b400c04fd430c8:3");
    }

    [Fact]
    public void IdempotencyKey_WithoutEdition_UsesEmptyEditionSegment()
    {
        var rootBytes = new byte[16];
        var source = new Cover
        {
            Domain = "order",
            Root = new UUID { Value = ByteString.CopyFrom(rootBytes) },
        };
        var deferred = new AngzarrDeferredSequence { Source = source, SourceSeq = 1 };

        Helpers.IdempotencyKey(deferred).Should().Be(":order:00000000000000000000000000000000:1");
    }

    // --------------------------------------------------------------------
    // DestinationMap — cross-language alias for proto_ext::destination_map
    // --------------------------------------------------------------------

    [Fact]
    public void DestinationMap_KeysByRootHex()
    {
        var rootA = new byte[16];
        rootA[0] = 0xAA;
        var rootB = new byte[16];
        rootB[0] = 0xBB;

        var bookA = new EventBook
        {
            Cover = new Cover { Root = new UUID { Value = ByteString.CopyFrom(rootA) } },
        };
        var bookB = new EventBook
        {
            Cover = new Cover { Root = new UUID { Value = ByteString.CopyFrom(rootB) } },
        };

        var map = Helpers.DestinationMap(new[] { bookA, bookB });

        map.Should().ContainKey("aa000000000000000000000000000000");
        map.Should().ContainKey("bb000000000000000000000000000000");
    }

    [Fact]
    public void DestinationMap_SkipsRootless()
    {
        // Entries without root or cover are silently skipped.
        var bookWith = new EventBook
        {
            Cover = new Cover { Root = new UUID { Value = ByteString.CopyFrom(new byte[16]) } },
        };
        var bookWithout = new EventBook { Cover = new Cover() };
        var bookCoverless = new EventBook();

        var map = Helpers.DestinationMap(new[] { bookWith, bookWithout, bookCoverless });

        map.Should().HaveCount(1);
        map.Should().ContainKey("00000000000000000000000000000000");
    }
}
