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
            request.PropertyName,
            request.PropertyAddress,
            request.CleaningDate,
            request.CleaningDuration
        );

        // Create MIME message with ICS attachment
        var rawMessage = CreateRawEmailWithAttachment(
            request.FromEmail,
            request.ToEmail,
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

    private string GenerateIcsContent(string cleanerName, string propertyName, string propertyAddress, string cleaningDate, string duration)
    {
        // Parse date and calculate end time
        var startDate = DateTime.Parse(cleaningDate);
        var startDateTime = startDate.AddHours(12); // 12:00 PM
        
        // Parse duration (e.g., "2-3 hours" -> use 2.5 hours)
        var durationHours = ParseDuration(duration);
        var endDateTime = startDateTime.AddHours(durationHours);

        var now = DateTime.UtcNow;
        var uid = Guid.NewGuid().ToString();

        return $@"BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//RentalTurnManager//Calendar//EN
METHOD:REQUEST
BEGIN:VEVENT
UID:{uid}
DTSTAMP:{FormatDateTime(now)}
DTSTART:{FormatDateTime(startDateTime)}
DTEND:{FormatDateTime(endDateTime)}
SUMMARY:Cleaning - {propertyName}
DESCRIPTION:Cleaning and laundry turnover for {propertyName}
LOCATION:{propertyAddress}
STATUS:CONFIRMED
SEQUENCE:0
BEGIN:VALARM
TRIGGER:-PT1H
ACTION:DISPLAY
DESCRIPTION:Reminder: Cleaning in 1 hour
END:VALARM
END:VEVENT
END:VCALENDAR";
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

    private string CreateRawEmailWithAttachment(string from, string to, string subject, string htmlBody, string icsContent, string filename)
    {
        var boundary = $"----=_Part_{Guid.NewGuid():N}";

        var message = new StringBuilder();
        message.AppendLine($"From: {from}");
        message.AppendLine($"To: {to}");
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
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string CleanerName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public string PropertyAddress { get; set; } = string.Empty;
    public string CleaningDate { get; set; } = string.Empty;
    public string CleaningDuration { get; set; } = string.Empty;
}
