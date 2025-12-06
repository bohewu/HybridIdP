namespace Web.IdP.Services;

public class DeviceVerificationViewModel
{
    public string? UserCode { get; set; }
    public string? ApplicationName { get; set; }
    public string? Scope { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
}
