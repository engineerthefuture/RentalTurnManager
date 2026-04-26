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
using Moq;
using FluentAssertions;
using System.Text;
using Amazon.Lambda.TestUtilities;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Amazon.S3;
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
    public void BuildCleaningEventUid_PropertyNameWithAddressInParens_SpecialCharsStripped()
    {
        // Real-world property names include the address in parentheses, e.g.
        // "Waterfront Lake Anna (157 Cooke Ln, Bumpass, VA 23024)".
        // Commas and parentheses in iCalendar TEXT values must be escaped, and
        // Apple Calendar can misparse unescaped commas in a UID — causing it to
        // navigate to an existing (older) event instead of offering to add a new one.
        var dt = new DateTime(2026, 3, 29, 15, 30, 0, DateTimeKind.Utc);

        var uid = Function.BuildCleaningEventUid(
            "Waterfront Lake Anna (157 Cooke Ln, Bumpass, VA 23024)", dt, "HM4JQC3K94");

        // No parentheses, commas, or other non-[A-Za-z0-9-] chars in the UID.
        uid.Should().Be(
            "cleaning-Waterfront-Lake-Anna-157-Cooke-Ln-Bumpass-VA-23024-20260329-HM4JQC3K94@rentalturnmanager.com");
        uid.Should().NotContainAny("(", ")", ",");
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

    [Fact]
    public void ParseCleaningDateTimeUtc_CustomTimezone_NoonInThatTimezoneUsedAsFallback()
    {
        // "America/Chicago" CDT on Sept 26, 2026 is UTC-5, so noon = 17:00 UTC
        var result = Function.ParseCleaningDateTimeUtc(null, "2026-09-26", "America/Chicago");

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Date.Should().Be(new DateTime(2026, 9, 26));
        result.Hour.Should().Be(17); // 12:00 PM CDT = 17:00 UTC
    }

    [Fact]
    public void ParseCleaningDateTimeUtc_InvalidTimezone_FallsBackToUtc()
    {
        // An unrecognised timezone ID safely falls back to UTC, so noon date-only
        // fallback lands at 12:00 UTC rather than raising an exception.
        var result = Function.ParseCleaningDateTimeUtc(null, "2026-09-26", "Not/A/Valid/Zone");

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Hour.Should().Be(12); // noon UTC
    }

    [Fact]
    public void ParseCleaningDateTimeUtc_BothParamsNull_ReturnsCurrentUtcTime()
    {
        var result = Function.ParseCleaningDateTimeUtc(null, null);

        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

// ---------------------------------------------------------------------------
// ParseDuration
// ---------------------------------------------------------------------------

public class CalendarFunctionParseDurationTests
{
    [Theory]
    [InlineData("2-3 hours", 2.5)]
    [InlineData("1-2 hours", 1.5)]
    [InlineData("3 hours", 3.0)]
    [InlineData("2.25 hours", 2.25)]
    [InlineData("1.5 hours", 1.5)]
    [InlineData("0.5", 0.5)]
    public void ParseDuration_ValidInput_ReturnsExpectedHours(string input, double expected)
    {
        Function.ParseDuration(input).Should().BeApproximately(expected, 0.001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no numbers here")]
    public void ParseDuration_NoNumericContent_ReturnsDefaultTwoHours(string input)
    {
        Function.ParseDuration(input).Should().BeApproximately(2.0, 0.001);
    }
}

// ---------------------------------------------------------------------------
// FunctionHandler — integration tests via mocked SES
// ---------------------------------------------------------------------------

public class CalendarFunctionHandlerTests
{
    /// <summary>
    /// Invokes the CalendarLambda FunctionHandler with a mocked SES client and returns
    /// the full raw MIME message that would have been sent via SES.
    /// </summary>
    private static async Task<string> InvokeAndCaptureRawEmail(CalendarEmailRequest request)
    {
        var sesMock = new Mock<IAmazonSimpleEmailService>();
        var s3Mock  = new Mock<IAmazonS3>();

        SendRawEmailRequest? captured = null;
        sesMock
            .Setup(s => s.SendRawEmailAsync(
                It.IsAny<SendRawEmailRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<SendRawEmailRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new SendRawEmailResponse { MessageId = "test-id" });

        var fn = new Function(sesMock.Object, s3Mock.Object);
        await fn.FunctionHandler(request, new TestLambdaContext());

        captured.Should().NotBeNull("SES must be called once to send the calendar email");
        return Encoding.UTF8.GetString(captured!.RawMessage.Data.ToArray());
    }

    private static CalendarEmailRequest InviteRequest() => new()
    {
        FromEmail       = "owner@example.com",
        ToEmail         = "cleaner@example.com",
        Subject         = "Cleaning Appointment",
        HtmlBody        = "<p>Cleaning scheduled</p>",
        CleanerName     = "Luis Dias",
        CleanerEmail    = "cleaner@example.com",
        CleanerPhone    = "+1-555-0100",
        OwnerName       = "Brent Foster",
        OwnerEmail      = "owner@example.com",
        PropertyName    = "My Lakehouse",
        PropertyAddress = "123 Lake Rd",
        CleaningDate    = "2026-09-26",
        CleaningDateTime = "2026-09-26T15:30:00Z",
        CleaningDuration = "2.25 hours",
        Timezone        = "America/New_York",
        BookingReference = "ABC123",
        IsCancellation  = false,
    };

    private static CalendarEmailRequest CancellationRequest() => new()
    {
        FromEmail       = "owner@example.com",
        ToEmail         = "cleaner@example.com",
        Subject         = "Cleaning Cancelled",
        HtmlBody        = "<p>Cancelled</p>",
        CleanerName     = "Luis Dias",
        CleanerEmail    = "cleaner@example.com",
        OwnerName       = "Brent Foster",
        OwnerEmail      = "owner@example.com",
        PropertyName    = "My Lakehouse",
        PropertyAddress = "123 Lake Rd",
        CleaningDate    = "2026-09-26",
        CleaningDateTime = "2026-09-26T15:30:00Z",
        CleaningDuration = "2.25 hours",
        Timezone        = "America/New_York",
        BookingReference = "ABC123",
        IsCancellation  = true,
    };

    // ---- Invite ICS structure ----

    [Fact]
    public async Task FunctionHandler_InviteRequest_IcsHasMethodRequestAndConfirmedStatus()
    {
        var rawEmail = await InvokeAndCaptureRawEmail(InviteRequest());

        rawEmail.Should().Contain("METHOD:REQUEST");
        rawEmail.Should().Contain("STATUS:CONFIRMED");
        rawEmail.Should().Contain("SEQUENCE:0");
    }

    [Fact]
    public async Task FunctionHandler_InviteRequest_IcsOrganizerAndAttendeesUseDynamicNames()
    {
        var rawEmail = await InvokeAndCaptureRawEmail(InviteRequest());

        rawEmail.Should().Contain("ORGANIZER;CN=Brent Foster:mailto:owner@example.com");
        rawEmail.Should().Contain("ATTENDEE;CN=Luis Dias;ROLE=REQ-PARTICIPANT:mailto:cleaner@example.com");
        rawEmail.Should().Contain("ATTENDEE;CN=Brent Foster;ROLE=OPT-PARTICIPANT:mailto:owner@example.com");
    }

    [Fact]
    public async Task FunctionHandler_InviteRequest_IcsContainsSummaryLocationAndUid()
    {
        var rawEmail = await InvokeAndCaptureRawEmail(InviteRequest());

        rawEmail.Should().Contain("SUMMARY:Cleaning - My Lakehouse");
        rawEmail.Should().Contain("LOCATION:123 Lake Rd");
        rawEmail.Should().Contain("UID:cleaning-My-Lakehouse-20260926-ABC123@rentalturnmanager.com");
    }

    // ---- Cancellation ICS structure ----

    [Fact]
    public async Task FunctionHandler_CancellationRequest_IcsHasMethodCancelAndCancelledStatus()
    {
        var rawEmail = await InvokeAndCaptureRawEmail(CancellationRequest());

        rawEmail.Should().Contain("METHOD:CANCEL");
        rawEmail.Should().Contain("STATUS:CANCELLED");
        rawEmail.Should().Contain("SEQUENCE:1");
    }

    [Fact]
    public async Task FunctionHandler_CancellationRequest_IcsOrganizerUsesDynamicOwnerName()
    {
        // Regression test: organizer name was previously hardcoded as "CN=Owner"
        var rawEmail = await InvokeAndCaptureRawEmail(CancellationRequest());

        rawEmail.Should().Contain("ORGANIZER;CN=Brent Foster:mailto:owner@example.com");
        rawEmail.Should().NotContain("CN=Owner");
    }

    [Fact]
    public async Task FunctionHandler_CancellationRequest_IcsUidMatchesInviteUid()
    {
        // The cancellation UID must match the invite UID exactly so calendar clients
        // can remove the existing event rather than presenting an "Add to Calendar" prompt.
        const string expectedUid = "UID:cleaning-My-Lakehouse-20260926-ABC123@rentalturnmanager.com";

        var inviteEmail = await InvokeAndCaptureRawEmail(InviteRequest());
        var cancelEmail = await InvokeAndCaptureRawEmail(CancellationRequest());

        inviteEmail.Should().Contain(expectedUid);
        cancelEmail.Should().Contain(expectedUid);
    }

    // ---- HTML time conversion ----

    [Fact]
    public async Task FunctionHandler_HtmlBodyWithTimePattern_TimeIsConvertedToLocalTimezone()
    {
        var req = InviteRequest();
        req.HtmlBody        = "<ul><li>Time: 15:30</li></ul>";
        req.CleaningDateTime = "2026-09-26T15:30:00Z";
        req.Timezone        = "America/New_York"; // EDT = UTC-4 → 11:30 AM

        var rawEmail = await InvokeAndCaptureRawEmail(req);

        rawEmail.Should().Contain("Time: 11:30 AM").And.NotContain("Time: 15:30");
    }

    [Fact]
    public async Task FunctionHandler_HtmlBodyWithCustomTimezone_UsesCorrectLocalTime()
    {
        var req = InviteRequest();
        req.HtmlBody        = "<ul><li>Time: 15:30</li></ul>";
        req.CleaningDateTime = "2026-09-26T15:30:00Z";
        req.Timezone        = "America/Chicago"; // CDT = UTC-5 → 10:30 AM

        var rawEmail = await InvokeAndCaptureRawEmail(req);

        rawEmail.Should().Contain("Time: 10:30 AM");
    }

    // ---- MIME structure ----

    [Fact]
    public async Task FunctionHandler_InviteRequest_CalendarPartAppearsAtMultipartMixedLevel()
    {
        var rawEmail = await InvokeAndCaptureRawEmail(InviteRequest());

        // The calendar attachment must appear at multipart/mixed level — outside the nested
        // multipart/alternative block — so Apple Calendar, Google Calendar, and Outlook
        // trigger the "Add to Calendar" invitation UI rather than treating it as a download.
        rawEmail.Should().Contain("Content-Type: text/calendar; charset=UTF-8; method=REQUEST");

        // The alt section uses "----=_Alt_" boundaries; the last occurrence is its closing marker.
        // text/calendar must appear after that closing marker.
        var lastAltBoundaryIdx  = rawEmail.LastIndexOf("----=_Alt_");
        var calContentTypeIdx   = rawEmail.IndexOf("Content-Type: text/calendar");
        calContentTypeIdx.Should().BeGreaterThan(lastAltBoundaryIdx,
            "text/calendar must appear after multipart/alternative closes");
    }

    [Fact]
    public async Task FunctionHandler_CancellationRequest_CalendarContentTypeHasMethodCancel()
    {
        var rawEmail = await InvokeAndCaptureRawEmail(CancellationRequest());

        rawEmail.Should().Contain("Content-Type: text/calendar; charset=UTF-8; method=CANCEL");
    }
}
