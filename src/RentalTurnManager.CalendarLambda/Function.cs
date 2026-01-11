/************************
 * Rental Turn Manager
 * Function.cs (Calendar Lambda)
 * 
 * AWS Lambda function that generates and sends calendar invites for
 * cleaning appointments. Creates ICS calendar files with proper timezone
 * handling and sends them via Amazon SES to cleaners and property owners.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Amazon.Lambda.Core;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using System.Text;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentalTurnManager.CalendarLambda;

public class Function
{
    private readonly IAmazonSimpleEmailService _sesClient;

    public Function()
    {
        _sesClient = new AmazonSimpleEmailServiceClient();
    }

    public Function(IAmazonSimpleEmailService sesClient)
    {
        _sesClient = sesClient;
    }

    public async Task<object> FunctionHandler(CalendarEmailRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation($"Generating calendar invite email for {request.ToEmail}");

        // Generate ICS content
        var icsContent = GenerateIcsContent(
            request.CleanerName,
            request.CleanerEmail,
            request.CleanerPhone,
            request.OwnerName,
            request.OwnerEmail,
            request.PropertyName,
            request.PropertyAddress,
            request.CleaningDate,
            request.CleaningDuration
        );

        // Create MIME message with ICS attachment
        var rawMessage = CreateRawEmailWithAttachment(
            request.FromEmail,
            request.ToEmail,
            request.CcEmail,
            request.Subject,
            request.HtmlBody,
            icsContent,
            $"cleaning-{request.CleaningDate}.ics"
        );

        // Send via SES
        await _sesClient.SendRawEmailAsync(new SendRawEmailRequest
        {
            RawMessage = new RawMessage
            {
                Data = new MemoryStream(Encoding.UTF8.GetBytes(rawMessage))
            }
        });

        context.Logger.LogInformation("Calendar invite email sent successfully");
        return new { Success = true };
    }

    private string GenerateIcsContent(string cleanerName, string cleanerEmail, string cleanerPhone, string ownerName, string ownerEmail, string propertyName, string propertyAddress, string cleaningDate, string duration)
    {
        // Parse date and treat it as Eastern Time, then set to 12:00 PM ET
        var startDate = DateTime.Parse(cleaningDate);
        var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        
        // Create a DateTime at 12:00 PM on the specified date in Eastern Time
        var startDateTimeUnspecified = new DateTime(startDate.Year, startDate.Month, startDate.Day, 12, 0, 0, DateTimeKind.Unspecified);
        var startDateTime = TimeZoneInfo.ConvertTimeToUtc(startDateTimeUnspecified, easternZone);
        
        // Parse duration (e.g., "2-3 hours" -> use 2.5 hours)
        var durationHours = ParseDuration(duration);
        var endDateTime = startDateTime.AddHours(durationHours);

        var now = DateTime.UtcNow;
        var uid = Guid.NewGuid().ToString();

        // Build description with cleaner contact info if provided
        var description = $"Cleaning and laundry turnover for {propertyName}";
        if (!string.IsNullOrEmpty(cleanerName))
        {
            description += $"\\nCleaner: {cleanerName}";
        }
        if (!string.IsNullOrEmpty(cleanerEmail))
        {
            description += $"\\nEmail: {cleanerEmail}";
        }
        if (!string.IsNullOrEmpty(cleanerPhone))
        {
            description += $"\\nPhone: {cleanerPhone}";
        }

        var icsBuilder = new StringBuilder();
        icsBuilder.AppendLine("BEGIN:VCALENDAR");
        icsBuilder.AppendLine("VERSION:2.0");
        icsBuilder.AppendLine("PRODID:-//RentalTurnManager//Calendar//EN");
        icsBuilder.AppendLine("METHOD:REQUEST");
        icsBuilder.AppendLine("BEGIN:VEVENT");
        icsBuilder.AppendLine($"UID:{uid}");
        icsBuilder.AppendLine($"DTSTAMP:{FormatDateTime(now)}");
        icsBuilder.AppendLine($"DTSTART:{FormatDateTime(startDateTime)}");
        icsBuilder.AppendLine($"DTEND:{FormatDateTime(endDateTime)}");
        icsBuilder.AppendLine($"SUMMARY:Cleaning - {propertyName}");
        icsBuilder.AppendLine($"DESCRIPTION:{description}");
        icsBuilder.AppendLine($"LOCATION:{propertyAddress}");
        
        // Add cleaner as attendee if email provided
        if (!string.IsNullOrEmpty(cleanerEmail))
        {
            icsBuilder.AppendLine($"ATTENDEE;CN={cleanerName};ROLE=REQ-PARTICIPANT:mailto:{cleanerEmail}");
        }
        
        // Add owner as optional attendee
        if (!string.IsNullOrEmpty(ownerEmail))
        {
            icsBuilder.AppendLine($"ATTENDEE;CN={ownerName};ROLE=OPT-PARTICIPANT:mailto:{ownerEmail}");
        }
        
        icsBuilder.AppendLine("STATUS:CONFIRMED");
        icsBuilder.AppendLine("SEQUENCE:0");
        icsBuilder.AppendLine("BEGIN:VALARM");
        icsBuilder.AppendLine("TRIGGER:-PT1H");
        icsBuilder.AppendLine("ACTION:DISPLAY");
        icsBuilder.AppendLine("DESCRIPTION:Reminder: Cleaning in 1 hour");
        icsBuilder.AppendLine("END:VALARM");
        icsBuilder.AppendLine("END:VEVENT");
        icsBuilder.AppendLine("END:VCALENDAR");
        
        return icsBuilder.ToString();
    }

    private double ParseDuration(string duration)
    {
        // Extract first number from duration string (e.g., "2-3 hours" -> 2.5)
        var match = System.Text.RegularExpressions.Regex.Match(duration, @"(\d+(?:\.\d+)?)\s*-\s*(\d+(?:\.\d+)?)");
        if (match.Success)
        {
            var min = double.Parse(match.Groups[1].Value);
            var max = double.Parse(match.Groups[2].Value);
            return (min + max) / 2.0;
        }

        // Try single number
        match = System.Text.RegularExpressions.Regex.Match(duration, @"(\d+(?:\.\d+)?)");
        if (match.Success)
        {
            return double.Parse(match.Groups[1].Value);
        }

        return 2.0; // Default 2 hours
    }

    private string FormatDateTime(DateTime dt)
    {
        return dt.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
    }

    private string CreateRawEmailWithAttachment(string from, string to, string cc, string subject, string htmlBody, string icsContent, string filename)
    {
        var boundary = $"----=_Part_{Guid.NewGuid():N}";

        var message = new StringBuilder();
        message.AppendLine($"From: {from}");
        message.AppendLine($"To: {to}");
        if (!string.IsNullOrEmpty(cc))
        {
            message.AppendLine($"Cc: {cc}");
        }
        message.AppendLine($"Subject: {subject}");
        message.AppendLine("MIME-Version: 1.0");
        message.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
        message.AppendLine();
        message.AppendLine($"--{boundary}");
        message.AppendLine("Content-Type: text/html; charset=UTF-8");
        message.AppendLine("Content-Transfer-Encoding: 7bit");
        message.AppendLine();
        message.AppendLine(htmlBody);
        message.AppendLine();
        message.AppendLine($"--{boundary}");
        message.AppendLine("Content-Type: text/calendar; charset=UTF-8; method=REQUEST");
        message.AppendLine($"Content-Disposition: attachment; filename=\"{filename}\"");
        message.AppendLine("Content-Transfer-Encoding: 7bit");
        message.AppendLine();
        message.AppendLine(icsContent);
        message.AppendLine();
        message.AppendLine($"--{boundary}--");

        return message.ToString();
    }
}

public class CalendarEmailRequest
{
    public string FromEmail { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string CcEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string CleanerName { get; set; } = string.Empty;
    public string CleanerEmail { get; set; } = string.Empty;
    public string CleanerPhone { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyAddress { get; set; } = string.Empty;
    public string CleaningDate { get; set; } = string.Empty;
    public string CleaningDuration { get; set; } = string.Empty;
}
