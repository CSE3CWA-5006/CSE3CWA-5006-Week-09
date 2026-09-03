using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Web;
using MicrosoftPilot.Models;
using MicrosoftPilot.Services;

namespace MicrosoftPilot.Pages;

// These scopes work with a personal Outlook.com Microsoft account.
[AuthorizeForScopes(ScopeKeySection = "Graph:StartupScopes")]
public class IndexModel(MicrosoftGraphService graphService, ReportService reportService) : PageModel
{
    // Report is null before the user clicks "Load today".
    public DashboardReport? Report { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Report = await graphService.BuildReportAsync(cancellationToken);
            return Page();
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            return ConsentChallenge();
        }
    }

    public async Task<IActionResult> OnPostDownloadDocxAsync(CancellationToken cancellationToken)
    {
        DashboardReport report;

        try
        {
            report = await graphService.BuildReportAsync(cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            return ConsentChallenge();
        }

        var bytes = reportService.CreateDocx(report);
        var fileName = $"microsoft-pilot-report-{DateTime.Now:yyyyMMdd-HHmm}.docx";

        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
    }

    public async Task<IActionResult> OnPostDownloadPdfAsync(CancellationToken cancellationToken)
    {
        DashboardReport report;

        try
        {
            report = await graphService.BuildReportAsync(cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            return ConsentChallenge();
        }

        var bytes = reportService.CreatePdf(report);
        var fileName = $"microsoft-pilot-report-{DateTime.Now:yyyyMMdd-HHmm}.pdf";

        return File(bytes, "application/pdf", fileName);
    }

    private ChallengeResult ConsentChallenge()
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Page("/Index")
        };

        // Force the Microsoft consent screen so newly added Graph scopes are
        // granted cleanly after app-registration changes.
        properties.SetParameter("prompt", "consent");

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }
}
