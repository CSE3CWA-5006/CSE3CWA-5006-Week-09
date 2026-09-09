using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MicrosoftPilot.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration lockdown.
//
// By default ASP.NET Core layers extra configuration sources on top of
// appsettings.json (User Secrets, environment variables, command line). That is
// how a developer's private Entra credentials can silently "leak" into a run
// even though appsettings.json still says YOUR_CLIENT_ID.
//
// For this teaching sample we want exactly one source of truth: the
// appsettings*.json files that ship inside the project. Everyone who runs it
// must put their OWN Entra app registration in appsettings.json.
// ---------------------------------------------------------------------------
foreach (var source in builder.Configuration.Sources
             .Where(s => s is not Microsoft.Extensions.Configuration.Json.JsonConfigurationSource j
                         || (j.Path ?? string.Empty).Contains("secrets.json", StringComparison.OrdinalIgnoreCase))
             .ToList())
{
    // Keep the chained host configuration so ASPNETCORE_ENVIRONMENT / content
    // root still resolve; drop user secrets, environment variables, CLI args.
    if (source is Microsoft.Extensions.Configuration.ChainedConfigurationSource)
    {
        continue;
    }

    builder.Configuration.Sources.Remove(source);
}

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

// Fail fast with a readable message instead of a confusing AADSTS error.
var clientId = builder.Configuration["AzureAd:ClientId"];
if (string.IsNullOrWhiteSpace(clientId) || clientId == "YOUR_CLIENT_ID")
{
    throw new InvalidOperationException(
        "AzureAd:ClientId is not configured. Open appsettings.json and set AzureAd:ClientId " +
        "(and TenantId / ClientSecret) to values from your own Microsoft Entra app registration. " +
        "See README.md for the registration steps.");
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
