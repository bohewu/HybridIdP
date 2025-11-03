var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "Cookies";
    options.DefaultChallengeScheme = "oidc";
})
.AddCookie("Cookies")
.AddOpenIdConnect("oidc", options =>
{
    options.Authority = "https://localhost:7035";
    options.ClientId = "test_client";
    options.ClientSecret = "test_secret";
    options.ResponseType = "code";
    options.UsePkce = true;
    options.SaveTokens = true;
    
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.Scope.Add("roles");
    
    options.RequireHttpsMetadata = true; // Using trusted dev cert
    options.GetClaimsFromUserInfoEndpoint = false; // OpenIddict doesn't require userinfo endpoint

    // Keep original JWT claim types (don't remap to WS-* URIs)
    options.MapInboundClaims = false;

    // Ensure client_secret is sent when redeeming the authorization code
    options.Events.OnAuthorizationCodeReceived = context =>
    {
        if (context.TokenEndpointRequest is not null)
        {
            context.TokenEndpointRequest.ClientSecret = "test_secret";
        }
        return Task.CompletedTask;
    };

    // Handle remote authentication failures (e.g., user denies consent)
    options.Events.OnRemoteFailure = context =>
    {
        // Check if the error is from the authorization endpoint
        if (context.Failure?.Message.Contains("access_denied") == true)
        {
            // User denied authorization
            context.Response.Redirect("/Account/AccessDenied");
            context.HandleResponse();
            return Task.CompletedTask;
        }

        if (context.Failure?.Message.Contains("invalid_") == true)
        {
            // OAuth/OIDC error (invalid_request, invalid_scope, etc.)
            context.Response.Redirect("/Account/AuthError?error=" + Uri.EscapeDataString(context.Failure.Message));
            context.HandleResponse();
            return Task.CompletedTask;
        }

        // For other errors, use default error handling
        context.Response.Redirect("/Home/Error");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
