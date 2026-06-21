# Microsoft Pilot

ASP.NET Core Razor Pages app for a personal Microsoft account. It signs in with Microsoft, reads Microsoft Graph data, and exports a local DOCX/PDF report.

## What It Reads

All Microsoft Graph calls are read-only.

- Today's Outlook mail: `Mail.Read`
- Scheduled calendar meetings without a local date filter: `Calendars.Read`
- Microsoft account profile/community snapshot: `User.Read`
- Latest modified Word `.docx` files from OneDrive: `Files.Read`

## Required Azure Portal Setup

Open the in-app `Setup` tab for the full checklist. The short version is:

- App registration platform: `Web`
- Supported accounts: personal Microsoft accounts enabled
- Local redirect URI: `https://localhost:5001/signin-oidc`
- Azure redirect URI: `https://YOUR-APP-SERVICE-NAME.azurewebsites.net/signin-oidc`
- Delegated Graph permissions: `User.Read`, `Mail.Read`, `Calendars.Read`, `Files.Read`
- App Service Authentication: `Off`

## Configuration Values

Do not store real secrets in `appsettings.json`. Use local user-secrets and Azure App Service environment variables.

Local development:

```powershell
cd E:\microsoftpilot
dotnet user-secrets set "AzureAd:TenantId" "consumers"
dotnet user-secrets set "AzureAd:ClientId" "YOUR_APPLICATION_CLIENT_ID"
dotnet user-secrets set "AzureAd:ClientSecret" "YOUR_CLIENT_SECRET_VALUE"
```

Azure App Service environment variables:

```text
AzureAd__TenantId = consumers
AzureAd__ClientId = YOUR_APPLICATION_CLIENT_ID
AzureAd__ClientSecret = YOUR_CLIENT_SECRET_VALUE
ASPNETCORE_ENVIRONMENT = Production
```

## Run Locally

```powershell
cd E:\microsoftpilot
dotnet run --launch-profile https
```

Open:

```text
https://localhost:5001
```

## Publish To Azure Linux Web App From Visual Studio Community

The project targets `.NET 10` because the Azure Linux Web App runtime is set to .NET 10.0.

In Azure Portal, create:

```text
Resource type: Web App
Publish: Code
Runtime stack: .NET 10
Operating System: Linux
Pricing plan: Free F1 if available
```

Then in Visual Studio Community:

```text
Right click MicrosoftPilot project
Publish
Add a publish profile
Azure
Azure App Service (Linux)
Select Existing
Choose your Linux Web App
Finish
Publish
```

If Visual Studio cannot find your subscription, use publish profile import:

```text
Azure Portal -> App Service -> Overview -> Download publish profile
Visual Studio -> Publish -> Import profile
Select the downloaded .PublishSettings file
Publish
```

After publish, test:

```text
https://YOUR-APP-SERVICE-NAME.azurewebsites.net/health
```

It should return:

```text
OK
```

Then open:

```text
https://YOUR-APP-SERVICE-NAME.azurewebsites.net
```

## Code Map

- `Pages/Index.cshtml`: Razor UI
- `Pages/Index.cshtml.cs`: button handlers
- `Pages/Privacy.cshtml`: Azure setup checklist
- `Services/MicrosoftGraphService.cs`: read-only Microsoft Graph calls
- `Services/ReportService.cs`: DOCX/PDF generation
- `Models/DashboardModels.cs`: simple shared data models
