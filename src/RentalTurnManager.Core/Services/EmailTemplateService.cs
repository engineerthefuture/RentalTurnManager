using System.Text;
using System.Web;
using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

public interface IEmailTemplateService
{
    string GenerateTimeButtonsHtml(List<TimeSlot> timeSlots, string callbackUrl, string token);
}

public class EmailTemplateService : IEmailTemplateService
{
    public string GenerateTimeButtonsHtml(List<TimeSlot> timeSlots, string callbackUrl, string token)
    {
        if (timeSlots == null || timeSlots.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("<p style=\"margin: 20px 0; padding: 15px; background-color: #f8f9fa; border-radius: 5px;\">");
        sb.Append("<strong style=\"display: block; margin-bottom: 10px;\">Or select an alternative time:</strong>");
        
        foreach (var slot in timeSlots)
        {
            var encodedTime = HttpUtility.UrlEncode(slot.IsoDateTime);
            sb.Append($"<a href=\"{callbackUrl}/respond?token={token}&response=yes&time={encodedTime}\" ");
            sb.Append("style=\"display: inline-block; background-color: #007bff; color: white; padding: 8px 20px; text-decoration: none; border-radius: 5px; margin: 5px;\">");
            sb.Append($"{slot.Time}</a>");
        }
        
        sb.Append("</p>");
        return sb.ToString();
    }
}
