using TestClient.Constants;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = AuthenticationSchemes.Cookies;
    options.DefaultChallengeScheme = AuthenticationSchemes.OpenIdConnect;
})
.AddCookie(AuthenticationSchemes.Cookies)
.AddOpenIdConnect(AuthenticationSchemes.OpenIdConnect, options =>
{
    options.Authority = "https://localhost:7035";
    // Align with seeded demo client (public client, no secret)
    options.ClientId = "demo-client-1";
    options.ResponseType = "code";
    options.UsePkce = true;
    options.SaveTokens = true;
    
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    
    options.RequireHttpsMetadata = true; // Using trusted dev cert
    options.GetClaimsFromUserInfoEndpoint = false; // OpenIddict doesn't require userinfo endpoint

    // Keep original JWT claim types (don't remap to WS-* URIs)
    options.MapInboundClaims = false;

    // Public client: no client secret injection needed

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
