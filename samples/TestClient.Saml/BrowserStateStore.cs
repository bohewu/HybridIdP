using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace TestClient.Saml;

// Single-process sample only. Restart invalidates pending flows; never run replicas.
public sealed class BrowserStateStore(IDataProtectionProvider provider)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (object Value, DateTimeOffset Expires)> _entries = new(StringComparer.Ordinal);
    private readonly IDataProtector _protector = provider.CreateProtector("SamlSample.Browser.v1");
    public static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public bool Add(string key, object value, DateTimeOffset expires)
    {
        lock (_gate)
        {
            foreach (var old in _entries.Where(pair => pair.Value.Expires <= DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray())
                _entries.Remove(old);
            if (_entries.Count >= 4096 || expires <= DateTimeOffset.UtcNow || expires > DateTimeOffset.UtcNow.AddMinutes(10)) return false;
            return _entries.TryAdd(key, (value, expires));
        }
    }

    public T? Find<T>(string key, bool take = false) where T : class
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry) || entry.Expires <= DateTimeOffset.UtcNow) return null;
            if (take) _entries.Remove(key);
            return entry.Value as T;
        }
    }

    public void BindBrowser(HttpContext context, string token, string nonce, string path, DateTimeOffset expires) =>
        context.Response.Cookies.Append("__Secure-SamlSample." + token, _protector.Protect(token + ":" + nonce), Cookie(path, expires));

    public bool MatchesBrowser(HttpContext context, string token, string nonce)
    {
        if (!context.Request.Cookies.TryGetValue("__Secure-SamlSample." + token, out var cookie)) return false;
        try { return _protector.Unprotect(cookie) == token + ":" + nonce; }
        catch (CryptographicException) { return false; }
    }

    public void ClearBrowser(HttpContext context, string token, string path) =>
        context.Response.Cookies.Delete("__Secure-SamlSample." + token, Cookie(path, null));

    private static CookieOptions Cookie(string path, DateTimeOffset? expires) => new()
    { HttpOnly = true, Secure = true, SameSite = SameSiteMode.None, IsEssential = true, Path = path, Expires = expires };
}

public sealed class DemoIdentityOptions
{
    public bool AllowSyntheticIdentity { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Subject { get; set; } = "";
}
