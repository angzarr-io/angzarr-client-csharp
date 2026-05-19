using FluentAssertions;
using Xunit;

namespace Angzarr.Client.Tests;

/// <summary>
/// Tests for Validation helpers.
///
/// <para>Audit finding #59 (structural error model). Errors carry a
/// SCREAMING_SNAKE Code (from <see cref="ErrorCodes"/>), a static Message
/// (from <see cref="ErrorMessages"/>), and structured Details
/// (keyed by <see cref="ErrorKeys"/>).</para>
///
/// <para>Audit finding #60: <c>RequireExists</c>/<c>RequireNotExists</c> take a
/// caller-supplied predicate boolean (e.g., <c>state != null</c>) so the
/// callsite is explicit about what "exists" means.</para>
///
/// <para>Audit finding #18 (Rust commit 2023261): <c>RequireExists</c> raises
/// NOT_FOUND, not just a generic rejection.</para>
/// </summary>
public class ValidationTests
{
    [Fact]
    public void RequirePositive_Negative_RaisesValueNotPositive()
    {
        Action act = () => Validation.RequirePositive(-1, "amount");

        var ex = act.Should().Throw<CommandRejectedError>().Which;
        ex.Code.Should().Be(ErrorCodes.ValueNotPositive);
        ex.Message.Should().Be(ErrorMessages.ValueNotPositive);
        ex.Details.Should().ContainKey(ErrorKeys.Field).WhoseValue.Should().Be("amount");
        ex.IsInvalidArgument().Should().BeTrue();
    }

    [Fact]
    public void RequirePositive_Zero_RaisesValueNotPositive()
    {
        Action act = () => Validation.RequirePositive(0, "amount");
        act.Should().Throw<CommandRejectedError>().Which.Code.Should().Be(ErrorCodes.ValueNotPositive);
    }

    [Fact]
    public void RequirePositive_Positive_DoesNotThrow()
    {
        Action act = () => Validation.RequirePositive(1, "amount");
        act.Should().NotThrow();
    }

    [Fact]
    public void RequireNonNegative_Negative_RaisesValueNotNonNegative()
    {
        Action act = () => Validation.RequireNonNegative(-1, "stock");

        var ex = act.Should().Throw<CommandRejectedError>().Which;
        ex.Code.Should().Be(ErrorCodes.ValueNotNonNegative);
        ex.Message.Should().Be(ErrorMessages.ValueNotNonNegative);
    }

    [Fact]
    public void RequireNonNegative_Zero_DoesNotThrow()
    {
        Action act = () => Validation.RequireNonNegative(0, "stock");
        act.Should().NotThrow();
    }

    [Fact]
    public void RequireNotEmpty_String_Empty_RaisesValueEmpty()
    {
        Action act = () => Validation.RequireNotEmpty("", "name");

        var ex = act.Should().Throw<CommandRejectedError>().Which;
        ex.Code.Should().Be(ErrorCodes.ValueEmpty);
        ex.Message.Should().Be(ErrorMessages.ValueEmpty);
    }

    [Fact]
    public void RequireNotEmpty_Collection_Empty_RaisesCollectionEmpty()
    {
        Action act = () => Validation.RequireNotEmpty(System.Array.Empty<int>(), "items");

        var ex = act.Should().Throw<CommandRejectedError>().Which;
        ex.Code.Should().Be(ErrorCodes.CollectionEmpty);
        ex.Message.Should().Be(ErrorMessages.CollectionEmpty);
    }

    [Fact]
    public void RequireExists_False_RaisesEntityNotFoundWithNotFoundStatus()
    {
        // Audit finding #18 (Rust 2023261): RequireExists raises NOT_FOUND.
        Action act = () => Validation.RequireExists(false, "order O-1");

        var ex = act.Should().Throw<CommandRejectedError>().Which;
        ex.Code.Should().Be(ErrorCodes.EntityNotFound);
        ex.Message.Should().Be(ErrorMessages.EntityNotFound);
        ex.StatusCode.Should().Be("NOT_FOUND");
        ex.IsNotFound().Should().BeTrue();
    }

    [Fact]
    public void RequireExists_True_DoesNotThrow()
    {
        Action act = () => Validation.RequireExists(true, "context");
        act.Should().NotThrow();
    }

    [Fact]
    public void RequireNotExists_True_RaisesEntityAlreadyExists()
    {
        Action act = () => Validation.RequireNotExists(true, "duplicate");

        var ex = act.Should().Throw<CommandRejectedError>().Which;
        ex.Code.Should().Be(ErrorCodes.EntityAlreadyExists);
        ex.Message.Should().Be(ErrorMessages.EntityAlreadyExists);
        ex.StatusCode.Should().Be("FAILED_PRECONDITION");
    }
}
