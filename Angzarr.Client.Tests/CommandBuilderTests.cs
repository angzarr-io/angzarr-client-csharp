using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for CommandBuilder covering the scenarios from command-builder.feature.
/// Uses Empty as a placeholder protobuf message since we need IMessage for WithCommand.
/// </summary>
public class CommandBuilderTests
{
    // Use Empty as a simple test message
    private static readonly Empty TestMessage = new Empty();

    [Fact]
    public void Build_WithExplicitFieldValues_ShouldSetAllFields()
    {
        // Given an AggregateClient connected to the coordinator (simulated with null)
        // When I build a command using CommandBuilder with explicit values
        var rootGuid = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var correlationId = "corr-123";
        var sequence = 5;

        var builder = new CommandBuilder(null!, "test", rootGuid)
            .WithCorrelationId(correlationId)
            .WithSequence(sequence)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);

        var command = builder.Build();

        // Then the resulting CommandBook should have the specified values
        command.Cover.Domain.Should().Be("test");
        Helpers.ProtoToUuid(command.Cover.Root).Should().Be(rootGuid);
        command.Cover.CorrelationId.Should().Be(correlationId);
        Helpers.SequenceNum(command.Pages[0]).Should().Be((uint)sequence);
        command.Pages[0].Command.TypeUrl.Should().Be("type.googleapis.com/test.TestCommand");
    }

    [Fact]
    public void Build_WithoutCorrelationId_ShouldAutoGenerateOne()
    {
        // Spec HIGH-3.1: rootless ctor removed; use the autoGenerateRoot
        // overload to materialize a UUIDv4 root client-side.
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);

        var command = builder.Build();

        command.Cover.CorrelationId.Should().NotBeNullOrEmpty();
        Guid.TryParse(command.Cover.CorrelationId, out _).Should().BeTrue();
    }

    [Fact]
    public void Build_ForNewAggregate_AutoGenerateProducesRoot()
    {
        // Spec HIGH-3.1: the "new aggregate" path always stamps a root
        // client-side (audit #67). The prior "no root" contract is
        // superseded.
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);

        var command = builder.Build();

        command.Cover.Root.Should().NotBeNull();
        command.Cover.Root.Value.ToByteArray().Length.Should().Be(16);
    }

    [Fact]
    public void Build_WithoutSequence_ShouldThrow()
    {
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true).WithCommand(
            "type.googleapis.com/test.TestCommand",
            TestMessage
        );

        var act = () => builder.Build();
        act.Should().Throw<InvalidArgumentError>();
    }

    [Fact]
    public void Build_WithSequenceZero_ShouldSucceed()
    {
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);

        var command = builder.Build();

        Helpers.SequenceNum(command.Pages[0]).Should().Be(0u);
    }

    [Fact]
    public void MethodChaining_ShouldReturnBuilder()
    {
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true);

        var result1 = builder.WithCorrelationId("chain-test");
        var result2 = result1.WithSequence(10);
        var result3 = result2.WithCommand("type.googleapis.com/test.TestCommand", TestMessage);

        result1.Should().BeSameAs(builder);
        result2.Should().BeSameAs(builder);
        result3.Should().BeSameAs(builder);

        var command = builder.Build();
        command.Cover.CorrelationId.Should().Be("chain-test");
        Helpers.SequenceNum(command.Pages[0]).Should().Be(10u);
    }

    [Fact]
    public void Build_WithProtobufMessage_ShouldSerializeCorrectly()
    {
        var typeUrl = "type.googleapis.com/google.protobuf.Empty";

        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand(typeUrl, TestMessage);

        var command = builder.Build();

        command.Pages[0].Command.TypeUrl.Should().Be(typeUrl);
        command.Pages[0].Command.Value.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithoutCommand_ShouldThrow()
    {
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true);
        var act = () => builder.Build();
        act.Should().Throw<InvalidArgumentError>();
    }

    // -------------------------------------------------------------------
    // Audit finding #20: CommandNew auto-generates a UUID v4 root.
    // Across all six language clients, aggregate roots are always
    // client-assigned. CommandNew materializes a fresh UUID rather than
    // leaving the cover root unset.
    // -------------------------------------------------------------------

    [Fact]
    public void CommandNew_AutoGeneratesUuidRoot()
    {
        // Using FromChannel-less factory because Connect is not used here.
        // CommandHandlerClient is null but Build() doesn't dereference it.
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);

        var command = builder.Build();

        // Auto-generated UUID v4 root is set on the cover.
        command.Cover.Root.Should().NotBeNull();
        var bytes = command.Cover.Root.Value.ToByteArray();
        bytes.Length.Should().Be(16);
        // Not all zeros (would indicate "no root set").
        bytes.Should().NotBeEquivalentTo(new byte[16]);
    }

    [Fact]
    public void CommandNew_GeneratesDifferentRootsAcrossCalls()
    {
        var builder1 = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);
        var builder2 = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);

        var cmd1 = builder1.Build();
        var cmd2 = builder2.Build();

        cmd1.Cover.Root.Should().NotBe(cmd2.Cover.Root);
    }

    [Fact]
    public void CommandNew_OnClient_AutoGeneratesUuidRoot()
    {
        // CommandHandlerClient.CommandNew(domain) must produce a builder
        // that already has an auto-generated UUID v4 root, per audit #20.
        // We test the builder API rather than constructing a CommandHandlerClient
        // (which would require a live channel).
        // The behavior is asserted via CommandBuilder.Build() producing
        // a non-null Cover.Root with 16 bytes.
        //
        // Round-trip via Helpers.ProtoToUuid/UuidToProto confirms it's a
        // valid 16-byte UUID.
        var builder = new CommandBuilder(null!, "test", autoGenerateRoot: true)
            .WithSequence(0)
            .WithCommand("type.googleapis.com/test.TestCommand", TestMessage);
        var cmd = builder.Build();

        var roundTripped = Helpers.ProtoToUuid(cmd.Cover.Root);
        roundTripped.Should().NotBe(Guid.Empty);
    }
}
