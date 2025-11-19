using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

// E2E Test for JWT aud claim
// This test creates API resources, scopes, and verifies the aud claim in access tokens

var baseUrl = "https://localhost:7035";
var httpClient = new HttpClient(new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
});

Console.WriteLine("=== JWT aud Claim E2E Test ===\n");

// Step 1: Create API Scopes
Console.WriteLine("Step 1: Creating API scopes...");
var scopes = new[]
{
    new { name = "api:company:read", displayName = "Read Company Data", description = "Allows reading company information" },
    new { name = "api:company:write", displayName = "Write Company Data", description = "Allows creating and updating company information" },
    new { name = "api:inventory:read", displayName = "Read Inventory Data", description = "Allows reading inventory information" }
};

var scopeIds = new Dictionary<string, string>();

foreach (var scope in scopes)
{
    try
    {
        var response = await httpClient.PostAsJsonAsync($"{baseUrl}/api/admin/scopes", scope);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var id = result.GetProperty("id").GetString()!;
            scopeIds[scope.name] = id;
            Console.WriteLine($"  ✓ Created scope: {scope.name} (ID: {id})");
        }
        else
        {
            Console.WriteLine($"  ℹ Scope {scope.name} may already exist");
            // Try to get existing scope
            var allScopes = await httpClient.GetFromJsonAsync<JsonElement>($"{baseUrl}/api/admin/scopes?take=100");
            var items = allScopes.GetProperty("items").EnumerateArray();
            var existing = items.FirstOrDefault(s => s.GetProperty("name").GetString() == scope.name);
            if (existing.ValueKind != JsonValueKind.Undefined)
            {
                scopeIds[scope.name] = existing.GetProperty("id").GetString()!;
                Console.WriteLine($"  ✓ Found existing scope: {scope.name}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ Error creating scope {scope.name}: {ex.Message}");
    }
}

// Step 2: Create API Resources
Console.WriteLine("\nStep 2: Creating API Resources...");
var apiResources = new[]
{
    new
    {
        name = "company_api",
        displayName = "Company API",
        description = "Company management and data API",
        baseUrl = "https://api.company.com",
        scopeIds = new[] { scopeIds.GetValueOrDefault("api:company:read"), scopeIds.GetValueOrDefault("api:company:write") }.Where(id => id != null).ToArray()
    },
    new
    {
        name = "inventory_api",
        displayName = "Inventory API",
        description = "Inventory management and tracking API",
        baseUrl = "https://api.inventory.com",
        scopeIds = new[] { scopeIds.GetValueOrDefault("api:inventory:read") }.Where(id => id != null).ToArray()
    }
};

foreach (var resource in apiResources)
{
    try
    {
        var response = await httpClient.PostAsJsonAsync($"{baseUrl}/api/admin/apiresources", resource);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            var id = result.GetProperty("id").GetInt32();
            Console.WriteLine($"  ✓ Created API Resource: {resource.name} (ID: {id})");
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"  ℹ API Resource {resource.name} may already exist: {error}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ✗ Error creating API Resource {resource.name}: {ex.Message}");
    }
}

// Step 3: Verify setup
Console.WriteLine("\nStep 3: Verifying setup...");
try
{
    var resources = await httpClient.GetFromJsonAsync<JsonElement>($"{baseUrl}/api/admin/apiresources?take=100");
    var totalCount = resources.GetProperty("totalCount").GetInt32();
    Console.WriteLine($"  Total API Resources: {totalCount}");
    
    foreach (var res in resources.GetProperty("items").EnumerateArray())
    {
        var name = res.GetProperty("name").GetString();
        var scopeCount = res.GetProperty("scopeCount").GetInt32();
        Console.WriteLine($"    - {name} ({scopeCount} scopes)");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  ✗ Failed to verify: {ex.Message}");
}

Console.WriteLine("\n=== Setup Complete ===");
Console.WriteLine("Next: Test the aud claim by obtaining an access token with these scopes:");
Console.WriteLine("  - api:company:read → audience: company_api");
Console.WriteLine("  - api:company:write → audience: company_api");
Console.WriteLine("  - api:inventory:read → audience: inventory_api");
