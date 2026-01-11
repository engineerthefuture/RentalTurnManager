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
using Amazon.S3;
using Amazon.S3.Model;
using System.Text;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentalTurnManager.CalendarLambda;

public class Function
{
    private readonly IAmazonSimpleEmailService _sesClient;
    private readonly IAmazonS3 _s3Client;

    public Function()
    {
        _sesClient = new AmazonSimpleEmailServiceClient();
        _s3Client = new AmazonS3Client();
    }

    public Function(IAmazonSimpleEmailService sesClient, IAmazonS3 s3Client)
    {
        _sesClient = sesClient;
        _s3Client = s3Client;
    }
    
    public Function(IAmazonSimpleEmailService sesClient)
    {
        _sesClient = sesClient;
        _s3Client = new AmazonS3Client();
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
            request.CleaningDateTime,
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
        
        // Update booking state with cleaner assignment if booking details provided
        if (!string.IsNullOrEmpty(request.Platform) && 
            !string.IsNullOrEmpty(request.BookingReference) && 
            !string.IsNullOrEmpty(request.BookingStateBucket))
        {
            await UpdateBookingWithCleanerAssignment(
                request.Platform,
                request.BookingReference,
                request.BookingStateBucket,
                request.CleanerName,
                request.CleanerEmail,
                request.CleanerPhone,
                request.CleaningDateTime ?? request.CleaningDate,
                context
            );
        }
        
        return new { Success = true };
    }
    
    private async Task UpdateBookingWithCleanerAssignment(
        string platform,
        string bookingReference,
        string bucketName,
        string cleanerName,
        string cleanerEmail,
        string cleanerPhone,
        string cleaningDate,
        ILambdaContext context)
    {
        try
        {
            var key = $"bookings/{platform}/{bookingReference}.json";
            
            // Retrieve existing booking
            var getRequest = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };
            
            var response = await _s3Client.GetObjectAsync(getRequest);
            string bookingJson;
            using (var reader = new StreamReader(response.ResponseStream))
            {
                bookingJson = await reader.ReadToEndAsync();
            }
            
            // Parse and update booking
            var booking = JsonSerializer.Deserialize<BookingState>(bookingJson);
            if (booking != null)
            {
                booking.AssignedCleanerName = cleanerName;
                booking.AssignedCleanerEmail = cleanerEmail;
                booking.AssignedCleanerPhone = cleanerPhone;
                booking.CleanerConfirmedAt = DateTime.UtcNow;
                
                // Parse the full cleaning DateTime (already in UTC from workflow)
                if (DateTime.TryParse(cleaningDate, out var cleaningDateTime))
                {
                    // If the parsed DateTime has time info, use it directly
                    // Otherwise default to the parsed date (preserving any time that was included)
                    booking.ScheduledCleaningTime = cleaningDateTime.Kind == DateTimeKind.Utc 
                        ? cleaningDateTime 
                        : DateTime.SpecifyKind(cleaningDateTime, DateTimeKind.Utc);
                }
                
                // Save updated booking
                var updatedJson = JsonSerializer.Serialize(booking, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                var putRequest = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    ContentBody = updatedJson,
                    ContentType = "application/json"
                };
                
                await _s3Client.PutObjectAsync(putRequest);
                context.Logger.LogInformation($"Updated booking state with cleaner assignment: {platform}/{bookingReference}");
            }
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error updating booking state: {ex.Message}");
            // Don't fail the calendar invite if state update fails
        }
    }

    private string GenerateIcsContent(string cleanerName, string cleanerEmail, string cleanerPhone, string ownerName, string ownerEmail, string propertyName, string propertyAddress, string cleaningDate, string? cleaningDateTime, string duration)
    {
        DateTime startDateTime;
        
        // If CleaningDateTime is provided (ISO format with time), use it
        if (!string.IsNullOrEmpty(cleaningDateTime) && DateTime.TryParse(cleaningDateTime, out var parsedDateTime))
        {
            // Assume the DateTime from workflow is already in UTC
            startDateTime = parsedDateTime.Kind == DateTimeKind.Utc 
                ? parsedDateTime 
                : DateTime.SpecifyKind(parsedDateTime, DateTimeKind.Utc);
        }
        else
        {
            // Fallback: Parse date and default to 12:00 PM Eastern Time
            var startDate = DateTime.Parse(cleaningDate);
            var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var startDateTimeUnspecified = new DateTime(startDate.Year, startDate.Month, startDate.Day, 12, 0, 0, DateTimeKind.Unspecified);
            startDateTime = TimeZoneInfo.ConvertTimeToUtc(startDateTimeUnspecified, easternZone);
        }
        
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
    public string? CleaningDateTime { get; set; }
    public string CleaningDuration { get; set; } = string.Empty;
    
    // Booking details for state update
    public string? Platform { get; set; }
    public string? BookingReference { get; set; }
    public string? BookingStateBucket { get; set; }
}

public class BookingState
{
    public string Platform { get; set; } = string.Empty;
    public string BookingReference { get; set; } = string.Empty;
    public string PropertyId { get; set; } = string.Empty;
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public int NumberOfGuests { get; set; }
    public string? AssignedCleanerName { get; set; }
    public string? AssignedCleanerEmail { get; set; }
    public string? AssignedCleanerPhone { get; set; }
    public DateTime? CleanerConfirmedAt { get; set; }
    public DateTime? ScheduledCleaningTime { get; set; }
}
