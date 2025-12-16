using System;

namespace Core.Domain.Models;

public class EmailMessage
{
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }

    public EmailMessage() { }

    public EmailMessage(string to, string subject, string body, bool isHtml = false)
    {
        To = to;
        Subject = subject;
        Body = body;
        IsHtml = isHtml;
    }
}
