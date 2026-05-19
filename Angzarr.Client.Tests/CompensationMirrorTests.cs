// Tests mirror Python's tests/test_compensation.py and Rust's
// src/compensation.rs #[cfg(test)] block. They exist to bring C#'s
// compensation test surface to structural parity with Py/Rs (per
// 2026-05-16 Phase E.v2 directive: "ensure that unit tests mirror
// rust/python").
//
// Naming: idiomatic C# xUnit (PascalCase, _ separates groups). One Py
// test = one [Fact]. Reference-source-of-truth is Py because Py's
// surface is larger; absences vs Py drive the additions.

using System.Collections.Generic;
using Angzarr.Client;
using Google.Protobuf;
using Xunit;

namespace Angzarr.Client.Tests;

public class CompensationContextMirrorTests
{
    private static Angzarr.Notification MakeRejectionNotification(
        Angzarr.CommandBook? rejectedCommand = null,
        string reason = "bad state")
    {
        var rej = new Angzarr.RejectionNotification { RejectionReason = reason };
        if (rejectedCommand != null)
        {
            rej.RejectedCommand = rejectedCommand;
        }
        var notif = new Angzarr.Notification
        {
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(rej),
        };
        return notif;
    }

    // ---- TestCompensationContextFromNotification group ----

    // Mirrors test_empty_notification_yields_defaults — empty Notification
    // yields zero/empty fields without throwing.
    [Fact]
    public void EmptyNotification_YieldsDefaults()
    {
        var ctx = CompensationContext.FromNotification(new Angzarr.Notification());
        Assert.Equal(0u, ctx.SourceEventSequence);
        Assert.Equal("", ctx.RejectionReason);
        Assert.Null(ctx.RejectedCommand);
        Assert.Null(ctx.SourceAggregate);
    }

    // Mirrors test_rejection_reason_extracted.
    [Fact]
    public void RejectionReason_Extracted()
    {
        var notif = MakeRejectionNotification(reason: "out of stock");
        var ctx = CompensationContext.FromNotification(notif);
        Assert.Equal("out of stock", ctx.RejectionReason);
    }

    // Mirrors test_rejected_command_extracted.
    [Fact]
    public void RejectedCommand_Extracted()
    {
        var cmd = new Angzarr.CommandBook { Cover = new Angzarr.Cover { Domain = "inventory" } };
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        Assert.NotNull(ctx.RejectedCommand);
        Assert.Equal("inventory", ctx.RejectedCommand!.Cover.Domain);
    }

    // Mirrors test_source_from_angzarr_deferred — full source extraction.
    [Fact]
    public void SourceFromAngzarrDeferred_PopulatesAggregateAndSeq()
    {
        var src = new Angzarr.Cover { Domain = "orders" };
        var cmd = new Angzarr.CommandBook();
        var page = new Angzarr.CommandPage
        {
            Header = new Angzarr.PageHeader
            {
                AngzarrDeferred = new Angzarr.AngzarrDeferredSequence
                {
                    Source = src,
                    SourceSeq = 42u,
                },
            },
        };
        cmd.Pages.Add(page);
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        Assert.Equal(42u, ctx.SourceEventSequence);
        Assert.NotNull(ctx.SourceAggregate);
        Assert.Equal("orders", ctx.SourceAggregate!.Domain);
    }

    // Mirrors test_no_deferred_header_yields_no_source — plain sequence header
    // (no AngzarrDeferred) leaves source null.
    [Fact]
    public void NoDeferredHeader_YieldsNoSource()
    {
        var cmd = new Angzarr.CommandBook();
        var page = new Angzarr.CommandPage
        {
            Header = new Angzarr.PageHeader { Sequence = 1u },
        };
        cmd.Pages.Add(page);
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        Assert.Null(ctx.SourceAggregate);
        Assert.Equal(0u, ctx.SourceEventSequence);
    }

    // Mirrors test_deferred_without_source_subfield — seq propagates even when
    // the deferred entry's source subfield is absent.
    [Fact]
    public void DeferredWithoutSourceSubfield_StillPropagatesSeq()
    {
        var cmd = new Angzarr.CommandBook();
        var page = new Angzarr.CommandPage
        {
            Header = new Angzarr.PageHeader
            {
                AngzarrDeferred = new Angzarr.AngzarrDeferredSequence { SourceSeq = 7u },
            },
        };
        cmd.Pages.Add(page);
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        Assert.Equal(7u, ctx.SourceEventSequence);
        Assert.Null(ctx.SourceAggregate);
    }

    // ---- TestCompensationContextProperties group ----

    // Mirrors test_rejected_command_type_none_when_no_rejected_command.
    [Fact]
    public void RejectedCommandType_NullWhenNoRejectedCommand()
    {
        var ctx = CompensationContext.FromNotification(new Angzarr.Notification());
        Assert.Null(ctx.RejectedCommandType);
    }

    // Mirrors test_rejected_command_type_none_when_no_command_in_page.
    [Fact]
    public void RejectedCommandType_NullWhenPageHasNoCommand()
    {
        var cmd = new Angzarr.CommandBook();
        cmd.Pages.Add(new Angzarr.CommandPage { Header = new Angzarr.PageHeader { Sequence = 1u } });
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        Assert.Null(ctx.RejectedCommandType);
    }

    // Mirrors test_dispatch_key_empty_without_rejected_command.
    [Fact]
    public void DispatchKey_EmptyWithoutRejectedCommand()
    {
        var ctx = CompensationContext.FromNotification(new Angzarr.Notification());
        Assert.Equal("", ctx.DispatchKey);
    }

    // Mirrors test_dispatch_key_empty_when_no_command_type — command page
    // present but no packed command inside.
    [Fact]
    public void DispatchKey_EmptyWhenNoCommandType()
    {
        var cmd = new Angzarr.CommandBook { Cover = new Angzarr.Cover { Domain = "inventory" } };
        cmd.Pages.Add(new Angzarr.CommandPage { Header = new Angzarr.PageHeader { Sequence = 1u } });
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        Assert.Equal("", ctx.DispatchKey);
    }

    // Mirrors test_dispatch_key_empty_when_no_cover — command packed but
    // no cover on the book.
    [Fact]
    public void DispatchKey_EmptyWhenNoCover()
    {
        var cmd = new Angzarr.CommandBook();
        var page = new Angzarr.CommandPage
        {
            Header = new Angzarr.PageHeader { Sequence = 1u },
            Command = Google.Protobuf.WellKnownTypes.Any.Pack(new Angzarr.Cover { Domain = "x" }),
        };
        cmd.Pages.Add(page);
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        Assert.Equal("", ctx.DispatchKey);
    }

    // Mirrors test_dispatch_key_domain_slash_command — happy path.
    [Fact]
    public void DispatchKey_DomainSlashCommand_OnSuccess()
    {
        var cmd = new Angzarr.CommandBook { Cover = new Angzarr.Cover { Domain = "inventory" } };
        var page = new Angzarr.CommandPage
        {
            Header = new Angzarr.PageHeader { Sequence = 1u },
            Command = Google.Protobuf.WellKnownTypes.Any.Pack(new Angzarr.Cover()),
        };
        cmd.Pages.Add(page);
        var notif = MakeRejectionNotification(rejectedCommand: cmd);
        var ctx = CompensationContext.FromNotification(notif);
        // C# proto package is `angzarr` (vs Python's `angzarr_client.proto.angzarr`).
        // Helpers.TypeNameFromUrl strips the type.googleapis.com/ prefix.
        Assert.Equal("inventory/angzarr.Cover", ctx.DispatchKey);
    }
}

public class CompensationAggregateHelpersMirrorTests
{
    // Mirrors test_delegate_to_framework_defaults.
    [Fact]
    public void DelegateToFramework_Defaults()
    {
        var resp = Compensation.DelegateToFramework("no compensation logic");
        Assert.NotNull(resp.Revocation);
        Assert.True(resp.Revocation.EmitSystemRevocation);
        Assert.False(resp.Revocation.SendToDeadLetterQueue);
        Assert.False(resp.Revocation.Escalate);
        Assert.False(resp.Revocation.Abort);
        Assert.Equal("no compensation logic", resp.Revocation.Reason);
    }

    // Mirrors test_delegate_to_framework_all_flags.
    [Fact]
    public void DelegateToFramework_AllFlagsOverridden()
    {
        var resp = Compensation.DelegateToFramework(
            "escalate!", emitSystemEvent: false, sendToDeadLetter: true, escalate: true, abort: true);
        Assert.False(resp.Revocation.EmitSystemRevocation);
        Assert.True(resp.Revocation.SendToDeadLetterQueue);
        Assert.True(resp.Revocation.Escalate);
        Assert.True(resp.Revocation.Abort);
        Assert.Equal("escalate!", resp.Revocation.Reason);
    }

    // Mirrors test_emit_compensation_events_returns_events — response carries
    // events payload, not revocation.
    [Fact]
    public void EmitCompensationEvents_ReturnsEventsCoverPreserved()
    {
        var book = new Angzarr.EventBook { Cover = new Angzarr.Cover { Domain = "orders" } };
        var resp = Compensation.EmitCompensationEvents(book);
        Assert.NotNull(resp.Events);
        Assert.Equal("orders", resp.Events.Cover.Domain);
    }
}

public class CompensationProcessManagerMirrorTests
{
    // Mirrors test_pm_delegate_default.
    [Fact]
    public void PmDelegateToFramework_DefaultsEmitSystemRevocationTrue()
    {
        var resp = Compensation.PmDelegateToFramework("no pm logic");
        Assert.Null(resp.ProcessEvents);
        Assert.NotNull(resp.Revocation);
        Assert.True(resp.Revocation.EmitSystemRevocation);
        Assert.Equal("no pm logic", resp.Revocation.Reason);
    }

    // Mirrors test_pm_delegate_no_system_event — overload accepting flag.
    [Fact]
    public void PmDelegateToFramework_OverrideEmitSystemRevocationFalse()
    {
        var resp = Compensation.PmDelegateToFramework("silent", emitSystemEvent: false);
        Assert.NotNull(resp.Revocation);
        Assert.False(resp.Revocation.EmitSystemRevocation);
    }
}

public class IsNotificationTypeUrlMirrorTests
{
    // Mirrors test_matches_canonical_notification_url (audit #58 spec-compliant
    // fully qualified name).
    [Fact]
    public void MatchesCanonicalNotificationUrl()
    {
        Assert.True(Helpers.IsNotificationTypeUrl(Helpers.NotificationTypeUrl));
    }

    // Mirrors test_rejects_pre_58_short_form.
    [Fact]
    public void RejectsPre58ShortForm()
    {
        Assert.False(Helpers.IsNotificationTypeUrl("type.googleapis.com/angzarr.Notification"));
    }

    // Mirrors test_rejects_unrelated_type_url.
    [Fact]
    public void RejectsUnrelatedTypeUrl()
    {
        Assert.False(Helpers.IsNotificationTypeUrl("type.googleapis.com/examples.Foo"));
    }

    // Mirrors test_rejects_empty_string.
    [Fact]
    public void RejectsEmptyString()
    {
        Assert.False(Helpers.IsNotificationTypeUrl(""));
    }

    // Mirrors test_case_sensitive — lower-case 'notification' must not match.
    [Fact]
    public void CaseSensitive_LowercaseNotificationDoesNotMatch()
    {
        Assert.False(Helpers.IsNotificationTypeUrl(
            "type.googleapis.com/angzarr_client.proto.angzarr.notification"));
    }
}
