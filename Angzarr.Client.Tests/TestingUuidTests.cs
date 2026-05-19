using Angzarr.Client.Testing;
using FluentAssertions;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for the deterministic-UUID test helpers.
///
/// Mirrors Python's <c>angzarr_client.testing.uuid</c> module and
/// Rust's <c>testing::uuid</c> module byte-for-byte.
/// </summary>
public class TestingUuidTests
{
    [Fact]
    public void DefaultTestNamespace_MatchesPython()
    {
        TestUuid
            .DefaultTestNamespace.ToString()
            .Should()
            .Be("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }

    [Fact]
    public void UuidFor_MatchesPython()
    {
        // Python:
        //   uuid_for("test-root", DEFAULT_TEST_NAMESPACE).hex()
        //   = "1596515c79c357278c0e26e3ccf145c6"
        var bytes = TestUuid.UuidFor("test-root");

        Convert.ToHexString(bytes).ToLowerInvariant().Should().Be("1596515c79c357278c0e26e3ccf145c6");
    }

    [Fact]
    public void UuidFor_IsDeterministic()
    {
        TestUuid.UuidFor("player-alice").Should().BeEquivalentTo(TestUuid.UuidFor("player-alice"));
    }

    [Fact]
    public void UuidObjFor_BytesEqual_UuidFor()
    {
        var obj = TestUuid.UuidObjFor("x");
        var bytes = TestUuid.UuidFor("x");

        Identity.ToProtoBytes(obj).Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public void UuidForDefault_EqualsExplicitNamespaceCall()
    {
        // Audit finding #50: the default-namespace ergonomic wrapper.
        TestUuid
            .UuidForDefault("alice")
            .Should()
            .BeEquivalentTo(TestUuid.UuidFor("alice", TestUuid.DefaultTestNamespace));
    }

    [Fact]
    public void UuidStrForDefault_EqualsExplicitNamespaceCall()
    {
        TestUuid
            .UuidStrForDefault("alice")
            .Should()
            .Be(TestUuid.UuidStrFor("alice", TestUuid.DefaultTestNamespace));
    }
}
