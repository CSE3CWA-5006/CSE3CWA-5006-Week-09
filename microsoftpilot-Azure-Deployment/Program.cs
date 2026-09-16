using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MicrosoftPilot.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Behind a reverse proxy (Azure App Service terminates TLS in front of the
// app), the incoming request is plain HTTP and carries X-Forwarded-Proto:
// https. Without honouring that header ASP.NET Core thinks the scheme is
// "http", so the OpenID Connect middleware would build an http:// redirect
// URI and Entra ID rejects it with "redirect_uri is not valid".
// ---------------------------------------------------------------------------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Azure App Service front ends are not loopback addresses, so accept the
    // forwarded headers from any proxy instead of the default loopback list.
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
});

// ---------------------------------------------------------------------------
// Configuration sources.
//
// ASP.NET Core's default layering is exactly what an Azure deployment needs, so
// this project deliberately does NOT strip any of it. In order of increasing
// priority:
//
//   1. appsettings.json                - non-secret defaults (TenantId, scopes)
//   2. appsettings.{Environment}.json  - optional per-environment overrides
//   3. User secrets                    - local development only
//   4. Environment variables           - Azure App Service application settings
//   5. Command line arguments          - last-resort overrides
//
// Source 4 wins over source 1, which is what keeps the client secret out of
// this repository. On Azure, set these App Service application settings instead
// of editing appsettings.json:
//
//   AzureAd__TenantId      = consumers
//   AzureAd__ClientId      = <application (client) id>
//   AzureAd__ClientSecret  = <client secret VALUE, not the secret ID>
//
// The double underscore is how an environment variable maps to ":" in a
// configuration key: AzureAd__ClientSecret -> AzureAd:ClientSecret.
// ---------------------------------------------------------------------------

// Fail fast with a readable message instead of a confusing AADSTS error the
// first time somebody opens the site.
var isPlaceholder = (string? value) =>
    string.IsNullOrWhiteSpace(value)
    || value.Contains("YOUR", StringComparison.OrdinalIgnoreCase)
    || value.StartsWith('<')
    || value.StartsWith('$');

var missingSettings = new[] { "AzureAd:ClientId", "AzureAd:ClientSecret" }
    .Where(key => isPlaceholder(builder.Configuration[key]))
    .ToList();

if (missingSettings.Count > 0)
{
    throw new InvalidOperationException(
        $"Entra configuration is missing or still a placeholder: {string.Join(", ", missingSettings)}. " +
        "For local runs set them in appsettings.json or with user secrets " +
        "(dotnet user-secrets set \"AzureAd:ClientSecret\" \"<value>\"). " +
        "On Azure set the App Service application settings AzureAd__ClientId and AzureAd__ClientSecret. " +
        "See README.md for the full checklist.");
}

// Ask only for personal-account-friendly read scopes. Teams access is not used
// in this version because it targets personal Microsoft accounts.
var graphScopes = builder.Configuration.GetSection("Graph:StartupScopes").Get<string[]>()
                  ?? Array.Empty<string>();

// Microsoft.Identity.Web handles the OpenID Connect sign-in flow and stores tokens
// in a simple in-memory cache for this teaching sample.
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi(graphScopes)
    .AddInMemoryTokenCaches();

// FallbackPolicy means every Razor Page requires a signed-in user unless a page
// explicitly opts out with [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// Controllers are needed for Microsoft.Identity.Web.UI sign-in/sign-out routes.
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

// Razor Pages are the main UI for this app.
builder.Services.AddRazorPages();

// HttpClient is the safest simple way to call Microsoft Graph REST endpoints.
builder.Services.AddHttpClient<MicrosoftGraphService>();
builder.Services.AddScoped<ReportService>();

// QuestPDF requires an explicit license choice. Community is suitable for small
// student/demo projects; check QuestPDF's license before using this commercially.
QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

// Must run before anything that inspects the request scheme (HTTPS redirection
// and the OpenID Connect challenge).
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

// Authentication must run before Authorization, otherwise [Authorize] cannot
// know who the current user is.
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();

// Azure App Service can call this lightweight endpoint for a health check.
// It is anonymous on purpose because it does not expose Microsoft 365 data.
app.MapGet("/health", () => Results.Ok("OK")).AllowAnonymous();

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
