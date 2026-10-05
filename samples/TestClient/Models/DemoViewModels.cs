namespace TestClient.Models;

public sealed record DemoHomeViewModel(string Authority, string ClientId, IReadOnlyList<string> Scopes);
public sealed record ClaimDisplayViewModel(string Type, string Value);
public sealed record TokenStatusViewModel(string Name, bool Present);
public sealed record ProfileViewModel(
    string Authority, string ClientId, string? DisplayName,
    IReadOnlyList<string> RequestedScopes,
    IReadOnlyList<ClaimDisplayViewModel> Claims,
    IReadOnlyList<string> AuthenticationMethods,
    DateTimeOffset? AuthenticatedAt, DateTimeOffset? AccessTokenExpiresAt,
    IReadOnlyList<TokenStatusViewModel> Tokens, int RefreshCount);
public sealed record ApiDemoViewModel(
    string Operation, bool Success, string Message, int? StatusCode = null,
    string? UserInfoJson = null, bool? RefreshTokenRotated = null,
    string? ExpectedProtocolError = null);
public sealed record RefreshDemoResult(
    bool Success, string Message, int? StatusCode = null, bool? RefreshTokenRotated = null);
