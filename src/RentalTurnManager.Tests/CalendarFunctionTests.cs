/************************
 * Rental Turn Manager
 * CalendarFunctionTests.cs
 *
 * Unit tests for CalendarLambda UID generation and date parsing helpers.
 * Covers sanitization, normalization, and invite/cancel correlation.
 *
 * Author: Brent Foster
 ***********************/

using Xunit;
using FluentAssertions;
using RentalTurnManager.CalendarLambda;

namespace RentalTurnManager.Tests;

public class CalendarFunctionTests
{
    // -------------------------------------------------------------------------
    // BuildCleaningEventUid
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildCleaningEventUid_WithoutBookingReference_OmitsBookingSegment()
    {
        var dt = new DateTime(2026, 9, 26, 15, 30, 0, DateTimeKind.Utc);

        var uid = Function.BuildCleaningEventUid("My Property", dt);

        uid.Should().Be("cleaning-My-Property-20260926@rentalturnmanager.com");
    }

    [Fact]
    public void BuildCleaningEventUid_WithBookingReference_AppendsSegment()
    {
        var dt = new DateTime(2026, 9, 26, 15, 30, 0, DateTimeKind.Utc);

        var uid = Function.BuildCleaningEventUid("My Property", dt, "HMYYRSZ9PH");

        uid.Should().Be("cleaning-My-Property-20260926-HMYYRSZ9PH@rentalturnmanager.com");
    }

    [Fact]
    public void BuildCleaningEventUid_BookingReferenceWithWhitespace_IsTrimmed()
    {
        var dt = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

        var uid = Function.BuildCleaningEventUid("Prop", dt, "  ABC123  ");

        uid.Should().Be("cleaning-Prop-20260926-ABC123@rentalturnmanager.com");
    }

    [Fact]
    public void BuildCleaningEventUid_BookingReferenceWithIllegalChars_IsSanitized()
    {
        var dt = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

        // CR, LF, '@', spaces, and other non-[A-Za-z0-9_-] characters must be stripped
        var uid = Function.BuildCleaningEventUid("Prop", dt, "AB\r\nC@1 2:3");

        uid.Should().Be("cleaning-Prop-20260926-ABC123@rentalturnmanager.com");
    }

    [Fact]
    public void BuildCleaningEventUid_BookingReferenceAllIllegalChars_OmitsSegment()
    {
        var dt = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

        var uid = Function.BuildCleaningEventUid("Prop", dt, "@@@\r\n");

        uid.Should().Be("cleaning-Prop-20260926@rentalturnmanager.com");
    }

    [Fact]
    public void BuildCleaningEventUid_NullBookingReference_OmitsSegment()
    {
        var dt = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

        var uid = Function.BuildCleaningEventUid("Prop", dt, null);

        uid.Should().Be("cleaning-Prop-20260926@rentalturnmanager.com");
    }

    [Fact]
    public void BuildCleaningEventUid_SameInputsYieldIdenticalUid_InviteAndCancelCorrelate()
    {
        var dt = new DateTime(2026, 9, 26, 15, 30, 0, DateTimeKind.Utc);
        const string bookingRef = "HMYYRSZ9PH";
        const string propertyName = "Waterfront Lake Anna";

        var inviteUid = Function.BuildCleaningEventUid(propertyName, dt, bookingRef);
        var cancelUid = Function.BuildCleaningEventUid(propertyName, dt, bookingRef);

        inviteUid.Should().Be(cancelUid);
    }

    // -------------------------------------------------------------------------
    // ParseCleaningDateTimeUtc
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseCleaningDateTimeUtc_UtcIsoString_ReturnsSameValue()
    {
        var result = Function.ParseCleaningDateTimeUtc("2026-09-26T15:30:00Z");

        result.Should().Be(new DateTime(2026, 9, 26, 15, 30, 0, DateTimeKind.Utc));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ParseCleaningDateTimeUtc_UnspecifiedKindString_TreatedAsUtc()
    {
        // A datetime without a timezone designator comes from the workflow as UTC
        var result = Function.ParseCleaningDateTimeUtc("2026-09-26T15:30:00");

        result.Should().Be(new DateTime(2026, 9, 26, 15, 30, 0, DateTimeKind.Utc));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ParseCleaningDateTimeUtc_DateOnlyFallback_DefaultsToNoonEastern()
    {
        // With no primary datetime string, the fallback date defaults to 12:00 PM Eastern
        var result = Function.ParseCleaningDateTimeUtc(null, "2026-09-26");

        // 12:00 PM Eastern is 16:00 UTC (EDT, UTC-4)
        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Date.Should().Be(new DateTime(2026, 9, 26));
        result.Hour.Should().Be(16); // 12:00 PM EDT = 16:00 UTC
    }

    [Fact]
    public void ParseCleaningDateTimeUtc_InviteAndCancelProduceSameDateSegment()
    {
        // Simulate invite using cleaningDateTime and cancel using the same value
        const string cleaningDateTime = "2026-09-26T15:30:00Z";

        var inviteDt = Function.ParseCleaningDateTimeUtc(cleaningDateTime, "09-26-2026");
        var cancelDt = Function.ParseCleaningDateTimeUtc(cleaningDateTime); // cancel receives CleaningDateTime ?? CleaningDate

        inviteDt.Date.Should().Be(cancelDt.Date,
            "invite and cancel must use the same date for UID correlation");
    }
}
