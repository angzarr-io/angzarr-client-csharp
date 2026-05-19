using Angzarr;
using FluentAssertions;
using Google.Protobuf;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for Destinations cross-language parity.
///
/// <para>Covers audit findings #31 (DeferredHeader static helper) and
/// #64 (MISSING_DESTINATION_SEQUENCE error code on stamp-without-domain).</para>
/// </summary>
public class DestinationsTests
{
    private static Destinations Build(params (string Domain, uint Seq)[] entries)
    {
        var dict = new Dictionary<string, uint>();
        foreach (var (d, s) in entries)
            dict[d] = s;
        return new Destinations(dict);
    }

    // ----------------------------------------------------------------
    // Audit #64 — stamp_command raises MISSING_DESTINATION_SEQUENCE
    //   with structured code/details when domain isn't registered.
    // ----------------------------------------------------------------

    [Fact]
    public void StampCommand_DomainMissing_ThrowsStructuralError()
    {
        var dests = Build(("inventory", 5));
        var cmd = new CommandBook { Cover = new Cover { Domain = "fulfillment" } };
        cmd.Pages.Add(new CommandPage { Command = new Google.Protobuf.WellKnownTypes.Any() });

        Action act = () => dests.StampCommand(cmd, "nonexistent");

        var ex = act.Should().Throw<InvalidArgumentError>().Which;
        ex.Code.Should().Be(ErrorCodes.MissingDestinationSequence);
        ex.Details.Should().ContainKey(ErrorKeys.Domain).WhoseValue.Should().Be("nonexistent");
        ex.Message.Should().Be(ErrorMessages.MissingDestinationSequence);
    }

    [Fact]
    public void StampCommand_DomainPresent_StampsAllPages()
    {
        var dests = Build(("inventory", 7));
        var cmd = new CommandBook { Cover = new Cover { Domain = "inventory" } };
        cmd.Pages.Add(new CommandPage { Command = new Google.Protobuf.WellKnownTypes.Any() });
        cmd.Pages.Add(new CommandPage { Command = new Google.Protobuf.WellKnownTypes.Any() });

        var stamped = dests.StampCommand(cmd, "inventory");

        stamped.Should().BeSameAs(cmd);
        cmd.Pages[0].Header.Sequence.Should().Be(7u);
        cmd.Pages[1].Header.Sequence.Should().Be(7u);
    }

    // ----------------------------------------------------------------
    // Audit #31 — Destinations.DeferredHeader static helper.
    // ----------------------------------------------------------------

    [Fact]
    public void DeferredHeader_BuildsAngzarrDeferredSequenceHeader()
    {
        var sourceCover = new Cover
        {
            Domain = "order",
            Root = new UUID { Value = ByteString.CopyFrom(new byte[16]) },
        };

        var header = Destinations.DeferredHeader(sourceCover, sourceSeq: 42);

        header.Should().NotBeNull();
        header.AngzarrDeferred.Should().NotBeNull();
        header.AngzarrDeferred.Source.Domain.Should().Be("order");
        header.AngzarrDeferred.SourceSeq.Should().Be(42u);
    }

    [Fact]
    public void DeferredHeader_PreservesSourceCoverFields()
    {
        var sourceCover = new Cover
        {
            Domain = "saga-x",
            Root = new UUID { Value = ByteString.CopyFrom(new byte[16]) },
            CorrelationId = "corr-7",
        };

        var header = Destinations.DeferredHeader(sourceCover, 1);

        header.AngzarrDeferred.Source.CorrelationId.Should().Be("corr-7");
    }

    // ----------------------------------------------------------------
    // P2.2 / Rust commit 4b78e97: Destinations preserves insertion order.
    // C# Dictionary<TK,TV> iteration is insertion-ordered (since .NET 6+),
    // so this works without IndexMap analog.
    // ----------------------------------------------------------------

    [Fact]
    public void Domains_PreservesInsertionOrder()
    {
        var dict = new Dictionary<string, uint>
        {
            ["c"] = 1,
            ["a"] = 2,
            ["b"] = 3,
        };
        var dests = new Destinations(dict);

        dests.Domains.Should().Equal(new[] { "c", "a", "b" });
    }

    [Fact]
    public void HasDomain_AliasOf_Has()
    {
        var dests = Build(("inventory", 1));
        dests.HasDomain("inventory").Should().BeTrue();
        dests.HasDomain("nope").Should().BeFalse();
    }
}
