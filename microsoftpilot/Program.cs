using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MicrosoftPilot.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
