namespace Core.Application.DTOs;

public class RemoveLoginRequest
{
    public string LoginProvider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
}
