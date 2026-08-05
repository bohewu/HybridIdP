namespace Core.Application;

public sealed class EmailDeliveryException : Exception
{
    public EmailDeliveryException(
        string code,
        string message,
        int? smtpStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        SmtpStatusCode = smtpStatusCode;
    }

    public string Code { get; }

    public int? SmtpStatusCode { get; }
}
