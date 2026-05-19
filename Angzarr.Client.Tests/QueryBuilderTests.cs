using FluentAssertions;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for QueryBuilder covering the scenarios from query-builder.feature.
/// </summary>
public class QueryBuilderTests
{
    [Fact]
    public void Build_WithDomainAndRoot_ShouldSetBothFields()
    {
        // When I build a query with domain and root
        var rootGuid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");

        var builder = new QueryBuilder(null!, "test", rootGuid);
        var query = builder.Build();

        // Then the resulting Query should have both fields set
        query.Cover.Domain.Should().Be("test");
        Helpers.ProtoToUuid(query.Cover.Root).Should().Be(rootGuid);
    }

    [Fact]
    public void Build_WithRangeTo_ShouldSetBothBounds()
    {
        // When I build a query with range from 5 to 10
        var builder = new QueryBuilder(null!, "test", Guid.NewGuid()).RangeTo(5, 10);

        var query = builder.Build();

        // Then the resulting Query should have sequence_range with lower=5 and upper=10
        query.Range.Should().NotBeNull();
        query.Range.Lower.Should().Be(5u);
        query.Range.Upper.Should().Be(10u);
    }

    [Fact]
    public void Build_WithRangeOpenEnded_ShouldOnlySetLowerBound()
    {
        // When I build a query with range from 5
        var builder = new QueryBuilder(null!, "test", Guid.NewGuid()).Range(5);

        var query = builder.Build();

        // Then the resulting Query should have sequence_range with lower=5 and no upper bound
        query.Range.Should().NotBeNull();
        query.Range.Lower.Should().Be(5u);
        query.Range.Upper.Should().Be(0u); // Default value when not set
    }

    [Fact]
    public void Build_AsOfSequence_ShouldSetTemporalSequence()
    {
        // When I build a query as_of_sequence 42
        var builder = new QueryBuilder(null!, "test", Guid.NewGuid()).AsOfSequence(42);

        var query = builder.Build();

        // Then the resulting Query should have temporal_query with sequence=42
        query.Temporal.Should().NotBeNull();
        query.Temporal.AsOfSequence.Should().Be(42u);
    }

    [Fact]
    public void Build_AsOfTime_ShouldParseTimestamp()
    {
        // When I build a query as_of_time "2024-01-15T10:30:00Z"
        var builder = new QueryBuilder(null!, "test", Guid.NewGuid()).AsOfTime(
            "2024-01-15T10:30:00Z"
        );

        var query = builder.Build();

        // Then the resulting Query should have temporal_query with the parsed timestamp
        query.Temporal.Should().NotBeNull();
        query.Temporal.AsOfTime.Should().NotBeNull();
        // January 15, 2024 10:30:00 UTC
        var expected = new DateTimeOffset(
            2024,
            1,
            15,
            10,
            30,
            0,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();
        query.Temporal.AsOfTime.Seconds.Should().Be(expected);
    }

    [Fact]
    public void Build_ByCorrelationId_ShouldClearRoot()
    {
        // When I build a query by_correlation_id "corr-456"
        var builder = new QueryBuilder(null!, "test", Guid.NewGuid()).ByCorrelationId("corr-456");

        var query = builder.Build();

        // Then the resulting Query should query by correlation_id
        query.Cover.CorrelationId.Should().Be("corr-456");
        // Root should be null when querying by correlation ID
        query.Cover.Root.Should().BeNull();
    }

    [Fact]
    public void Build_WithEdition_ShouldSetEditionName()
    {
        // When I build a query with_edition "v2"
        var builder = new QueryBuilder(null!, "test", Guid.NewGuid()).WithEdition("v2");

        var query = builder.Build();

        // Then the resulting Query should have edition "v2"
        query.Cover.Edition.Should().NotBeNull();
        query.Cover.Edition.Name.Should().Be("v2");
    }

    [Fact]
    public void AsOfTime_InvalidTimestamp_ShouldThrowSynchronously()
    {
        // Audit finding #34 (Option B — raise immediately): a malformed
        // rfc3339 string raises InvalidTimestampError synchronously rather
        // than deferring to Build(). Previously the failure was captured
        // into a sticky _error field that survived subsequent last-call-wins
        // setters, making `qb.AsOfTime("bad").AsOfSequence(5).Build()` raise
        // the stale parse error. Mirrors Rust's `as_of_time(...) -> Result<Self>`
        // where the bad call short-circuits at the call site.
        var qb = new QueryBuilder(null!, "test", Guid.NewGuid());

        Action act = () => qb.AsOfTime("not-a-timestamp");

        act.Should().Throw<InvalidTimestampError>();
    }

    [Fact]
    public void AsOfTime_InvalidTimestampAfterRange_DoesNotPoisonBuilder()
    {
        // After a synchronous-throw AsOfTime, the builder's prior range
        // selection survives and Build() succeeds. Mirrors Python's
        // sticky-error fix (audit finding #34).
        var qb = new QueryBuilder(null!, "test", Guid.NewGuid()).Range(7);

        try
        {
            qb.AsOfTime("not-a-timestamp");
        }
        catch (InvalidTimestampError)
        {
            // Expected.
        }

        // Builder is still usable — the prior range survives.
        var q = qb.Build();
        q.Range.Lower.Should().Be(7u);
    }

    // ---------------------------------------------------------------------
    // Audit finding #23 — last-selection-wins between Range / AsOfSequence /
    // AsOfTime. Mirrors Python's QueryBuilder.range/range_to/as_of_sequence/
    // as_of_time which clear the other slot on each call.
    // ---------------------------------------------------------------------

    [Fact]
    public void Range_AfterAsOfSequence_ClearsTemporal()
    {
        var qb = new QueryBuilder(null!, "test", Guid.NewGuid())
            .AsOfSequence(10)
            .Range(5);

        var q = qb.Build();

        q.Range.Should().NotBeNull();
        q.Range.Lower.Should().Be(5u);
        q.Temporal.Should().BeNull();
    }

    [Fact]
    public void AsOfSequence_AfterRange_ClearsRange()
    {
        var qb = new QueryBuilder(null!, "test", Guid.NewGuid())
            .Range(5)
            .AsOfSequence(42);

        var q = qb.Build();

        q.Temporal.Should().NotBeNull();
        q.Temporal.AsOfSequence.Should().Be(42u);
        q.Range.Should().BeNull();
    }

    [Fact]
    public void AsOfTime_AfterRange_ClearsRange()
    {
        var qb = new QueryBuilder(null!, "test", Guid.NewGuid())
            .Range(5)
            .AsOfTime("2024-01-15T10:30:00Z");

        var q = qb.Build();

        q.Temporal.Should().NotBeNull();
        q.Range.Should().BeNull();
    }

    [Fact]
    public void RangeTo_AfterTemporal_ClearsTemporal()
    {
        var qb = new QueryBuilder(null!, "test", Guid.NewGuid())
            .AsOfTime("2024-01-15T10:30:00Z")
            .RangeTo(3, 7);

        var q = qb.Build();

        q.Range.Should().NotBeNull();
        q.Range.Lower.Should().Be(3u);
        q.Range.Upper.Should().Be(7u); // Inclusive upper bound (audit #27).
        q.Temporal.Should().BeNull();
    }

    [Fact]
    public void Build_WithDomainOnly_ShouldNotRequireRoot()
    {
        // When I build a query with domain only (for correlation ID queries)
        var builder = new QueryBuilder(null!, "test").ByCorrelationId("corr-123");

        var query = builder.Build();

        // Then it should work without root
        query.Cover.Domain.Should().Be("test");
        query.Cover.CorrelationId.Should().Be("corr-123");
    }
}
