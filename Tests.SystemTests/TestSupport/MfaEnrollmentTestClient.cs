using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tests.SystemTests;

internal static class MfaEnrollmentTestClient
{
    public static async Task AuthorizeAsync(
        HttpClient client,
        string username,
        string password)
    {
        ArgumentNullException.ThrowIfNull(client);

        client.DefaultRequestHeaders.Authorization = null;
        await SignInAsync(client, username, password, "/Account/Login");
        await SetCsrfTokenAsync(client);

        var reauthenticationResponse =
            await client.PostAsync("/api/account/mfa/reauthenticate", null);
        reauthenticationResponse.EnsureSuccessStatusCode();
        var result =
            await reauthenticationResponse.Content.ReadFromJsonAsync<JsonElement>();
        var loginUrl = result.GetProperty("loginUrl").GetString();
        if (string.IsNullOrWhiteSpace(loginUrl))
        {
            throw new InvalidOperationException(
                "The MFA reauthentication endpoint did not return a login URL.");
        }

        await SignInAsync(client, username, password, loginUrl);
        await SetCsrfTokenAsync(client);
    }

    public static async Task SetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Account/Profile");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            @"meta\s+name=""csrf-token""\s+content=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(
                html,
                @"content=""([^""]+)""\s+name=""csrf-token""");
        }

        if (!match.Success)
        {
            throw new InvalidOperationException(
                "The profile page did not contain an antiforgery token.");
        }

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", match.Groups[1].Value);
    }

    public static async Task SignInAsync(
        HttpClient client,
        string username,
        string password,
        string loginUrl)
    {
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        var loginPageResponse = await client.GetAsync(loginUrl);
        loginPageResponse.EnsureSuccessStatusCode();
        var html = await loginPageResponse.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        if (!tokenMatch.Success)
        {
            tokenMatch = Regex.Match(
                html,
                @"value=""([^""]+)""\s+type=""hidden""\s+name=""__RequestVerificationToken""");
        }

        if (!tokenMatch.Success)
        {
            throw new InvalidOperationException(
                "The login page did not contain an antiforgery token.");
        }

        var loginResponse = await client.PostAsync(
            loginUrl,
            new FormUrlEncodedContent(
            [
                new("Input.Login", username),
                new("Input.Password", password),
                new("__RequestVerificationToken", tokenMatch.Groups[1].Value)
            ]));
        loginResponse.EnsureSuccessStatusCode();
    }
}
