using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MicrosoftPilot.Pages;

// Setup must be visible before Azure sign-in is fully configured, so it opts
// out of the global login requirement.
[AllowAnonymous]
public class PrivacyModel : PageModel
{
    public void OnGet()
    {
    }
}
