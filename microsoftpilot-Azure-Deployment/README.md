# Microsoft Pilot — Azure deployment edition

ASP.NET Core Razor Pages sample that signs in a **personal Microsoft account**, reads
Microsoft Graph read-only data (mail, calendar, profile, OneDrive `.docx` metadata) and
exports a local DOCX/PDF report.

This folder is the **online / Azure App Service** edition. It differs from the classroom
copy in three ways that only matter once the app runs behind a reverse proxy:

1. **No credentials in the repository.** The client secret is supplied by App Service
   application settings, never by `appsettings.json`.
2. **`X-Forwarded-Proto` is honoured**, so the OpenID Connect redirect URI is built as
   `https://…` even though the App Service front end hands the request to the app over HTTP.
   Without this, sign-in fails with `redirect_uri is not valid`.
3. Configuration layering is left at the ASP.NET Core default (appsettings → user secrets →
   environment variables → command line), which is what lets point 1 work.

> **Copyright (c) 2026 Dr Shuo Ding · <shuoding@outlook.com>**
> Released under the **GNU Affero General Public License v3.0 or later** — see [`LICENSE`](LICENSE).

## 1. Register the app in Microsoft Entra

`portal.azure.com` → **Microsoft Entra ID** → **App registrations** → **New registration**
(or reuse an existing one).

| Setting | Value |
|---|---|
| Supported account types | Accounts in any organizational directory and personal Microsoft accounts |
| Platform | **Web** |
| Redirect URI (Azure) | `https://<your-app-name>.azurewebsites.net/signin-oidc` |
| Redirect URI (local dev) | `https://localhost:5001/signin-oidc` |

Then, on the same registration:

- **Certificates & secrets** → **New client secret** → copy the **Value** (not the Secret ID).
  You only see it once.
- **API permissions** → add delegated Microsoft Graph permissions and grant consent:
  `User.Read`, `Mail.Read`, `Calendars.Read`, `Files.Read`.
- Leave App Service **Authentication** off; the app does its own OpenID Connect sign-in.

> The `<your-app-name>.azurewebsites.net` host must match the App Service you create in step 2.
> If the app was created with the "secure unique default hostname" preview, the real host looks
> like `myapp-a1b2c3d4.australiaeast-01.azurewebsites.net` — copy it from the App Service
> **Overview → Default domain**.

## 2. Create the App Service

`portal.azure.com` → **Create a resource** → **Web App**:

| Setting | Value |
|---|---|
| Publish | Code |
| Runtime stack | .NET 10 (LTS) |
| Operating system | Linux |
| Pricing plan | **Free F1** (`0.00` per month) |

Free F1 has a daily CPU quota (60 minutes). When it is used up the app is stopped until the
next day — there is no charge either way.

## 3. Configure the App Service (this is where the secret goes)

App Service → **Settings → Environment variables → App settings**, add:

| Name | Value |
|---|---|
| `AzureAd__TenantId` | `consumers` for personal accounts, `common` to accept both, or a tenant id |
| `AzureAd__ClientId` | application (client) id from step 1 |
| `AzureAd__ClientSecret` | client secret **value** from step 1 |

The double underscore maps to `:` in configuration, so `AzureAd__ClientSecret` fills in
`AzureAd:ClientSecret`. Restart the app after saving.

Optional, only if you enable App Service **Health check**:

| Name | Value |
|---|---|
| `WEBSITES_HEALTH_CHECK_PATH` | `/health` |

Do not put secrets in `appsettings.json`. That file ships with the deployment and is public
if you fork this repository.

## 4. Publish from Visual Studio

1. Open `MicrosoftPilot.slnx` / `MicrosoftPilot.csproj` in Visual Studio Community.
2. Right-click the project → **Publish**.
3. **Azure** → **Azure App Service (Linux)** → **Select Existing** → pick the App Service
   from step 2 → **Finish** → **Publish**.

If Visual Studio cannot list your subscription, use a publish profile instead:

```
Azure Portal → App Service → Overview → "…" → Download publish profile
Visual Studio → Publish → Import profile → select the .PublishSettings file
```

**Publish profiles authenticate with SCM basic auth, which new App Services disable by
default.** If publishing fails with 401/403, turn it on:

```
App Service → Configuration → General settings → SCM Basic Auth Publishing Credentials = On
```

Turn it back off afterwards if you do not need profile-based publishing.

From a terminal the same publish can be run head-less with the Visual Studio/MSBuild pipeline:

```powershell
msbuild MicrosoftPilot.csproj /t:Publish /p:Configuration=Release /p:PublishProfile=AzureAppService
```

`Properties/PublishProfiles/AzureAppService.pubxml.example` shows the shape of such a profile.
Copy it to `AzureAppService.pubxml` and fill in your own values — the real file is `.gitignore`d
because it contains the deployment password.

## 5. Verify

```
https://<your-app-name>.azurewebsites.net/health     -> OK
https://<your-app-name>.azurewebsites.net/           -> Microsoft sign-in
```

Signing in with a personal Microsoft account should return to the app and show the dashboard.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `redirect_uri is not valid`, and the URI in the address bar starts with `http://` | the app is not reading `X-Forwarded-Proto` | keep the `UseForwardedHeaders()` block at the top of `Program.cs` |
| `redirect_uri is not valid`, URI looks correct | the host is not registered on the app registration | add `https://<host>/signin-oidc` under **Authentication → Web** |
| `AADSTS700016` / app not found | wrong client id, or wrong `TenantId` for the account type | check `AzureAd__ClientId` and `AzureAd__TenantId` |
| App stops responding during the day | Free F1 daily CPU quota used up | wait for the quota reset or move to a Basic plan |
| Sign-in works, Graph calls return 403 | delegated permissions not consented | add the Graph permissions and grant user/admin consent |

## Files

| Path | Purpose |
|---|---|
| `Program.cs` | DI, forwarded headers, OpenID Connect, health endpoint |
| `Pages/Index.cshtml(.cs)` | dashboard and the Load / DOCX / PDF handlers |
| `Services/MicrosoftGraphService.cs` | all read-only Microsoft Graph calls |
| `Services/ReportService.cs` | DOCX (Open XML) and PDF (QuestPDF) generation |
| `Models/DashboardModels.cs` | shared rows used by the page and the reports |
| `Properties/PublishProfiles/` | publish profiles (the real one is `.gitignore`d) |

## Trademarks

Microsoft, Microsoft 365, Outlook, OneDrive, Microsoft Graph and Azure are trademarks of
Microsoft Corporation. MicrosoftPilot is an independent, unofficial project and is not
affiliated with, sponsored by, or endorsed by Microsoft.
