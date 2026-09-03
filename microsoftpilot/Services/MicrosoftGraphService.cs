using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Web;
using MicrosoftPilot.Models;

namespace MicrosoftPilot.Services;

// This service is the only class that talks directly to Microsoft Graph.
// Every method is read-only: it sends GET requests and never creates/updates data.
public sealed class MicrosoftGraphService(
    HttpClient httpClient,
    ITokenAcquisition tokenAcquisition,
    IConfiguration configuration)
{
    private const string GraphRoot = "https://graph.microsoft.com/v1.0";

    private string[] MailScopes => GetScopes("Graph:MailScopes", ["Mail.Read"]);
    private string[] CalendarScopes => GetScopes("Graph:CalendarScopes", ["Calendars.Read"]);
    private string[] FileScopes => GetScopes("Graph:FileScopes", ["Files.Read"]);
    private string[] ProfileScopes => GetScopes("Graph:ProfileScopes", ["User.Read"]);

    public async Task<IReadOnlyList<MailSummary>> GetTodaysMessagesAsync(CancellationToken cancellationToken)
    {
        // Graph stores receivedDateTime in UTC. We calculate "today" from the
        // server's local date, then convert that local range to UTC for filtering.
        var localStart = new DateTimeOffset(DateTime.Today);
        var localEnd = localStart.AddDays(1);
        var utcStart = localStart.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var utcEnd = localEnd.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

        var filter = $"receivedDateTime ge {utcStart} and receivedDateTime lt {utcEnd}";
        var url = $"{GraphRoot}/me/messages" +
                  "?$select=subject,from,receivedDateTime,isRead,importance,webLink" +
                  "&$orderby=receivedDateTime desc" +
                  "&$top=50" +
                  $"&$filter={Uri.EscapeDataString(filter)}";

        var values = await GetPagedValuesAsync(url, MailScopes, cancellationToken);

        return values.Select(item =>
        {
            var from = item.GetPropertyOrNull("from")?
                .GetPropertyOrNull("emailAddress")?
                .GetStringOrDefault("name") ?? "Unknown sender";

            return new MailSummary(
                Subject: item.GetStringOrDefault("subject", "(no subject)") ?? "(no subject)",
                From: from,
                ReceivedAt: item.GetDateTimeOffsetOrDefault("receivedDateTime"),
                IsRead: item.GetBoolOrDefault("isRead"),
                Importance: item.GetStringOrDefault("importance", "normal") ?? "normal",
                WebLink: item.GetStringOrDefault("webLink", null));
        }).ToList();
    }

    public async Task<IReadOnlyList<MeetingSummary>> GetScheduledMeetingsAsync(CancellationToken cancellationToken)
    {
        // /me/events reads calendar events without a date-range filter. The app
        // limits pages for a simple teaching sample, but does not impose start/end dates.
        var url = $"{GraphRoot}/me/events" +
                  "?$select=subject,organizer,start,end,location,isOnlineMeeting,webLink" +
                  "&$top=50";

        var values = await GetPagedValuesAsync(url, CalendarScopes, cancellationToken);

        return values.Select(item =>
            {
                var organizer = item.GetPropertyOrNull("organizer")?
                    .GetPropertyOrNull("emailAddress")?
                    .GetStringOrDefault("name") ?? "Unknown organizer";

                var location = item.GetPropertyOrNull("location")?
                    .GetStringOrDefault("displayName", "") ?? "";

                return new MeetingSummary(
                    Subject: item.GetStringOrDefault("subject", "(no subject)") ?? "(no subject)",
                    Organizer: organizer,
                    Start: item.GetGraphDateTimeOrDefault("start"),
                    End: item.GetGraphDateTimeOrDefault("end"),
                    Location: string.IsNullOrWhiteSpace(location) ? "No location" : location,
                    IsOnlineMeeting: item.GetBoolOrDefault("isOnlineMeeting"),
                    WebLink: item.GetStringOrDefault("webLink", null));
            })
            // /me/events also returns flights, hotels, package reminders, birthdays,
            // and other calendar items created from mail. For this teaching app,
            // "scheduled meeting" means a real meeting-like event, especially a
            // Teams/online meeting, not every calendar item.
            .Where(meeting =>
                meeting.IsOnlineMeeting ||
                meeting.Location.Contains("Teams", StringComparison.OrdinalIgnoreCase) ||
                meeting.Location.Contains("Meeting", StringComparison.OrdinalIgnoreCase) ||
                meeting.Subject.Contains("meeting", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<CommunitySummary> GetCommunityAsync(CancellationToken cancellationToken)
    {
        var root = await GetObjectAsync(
            $"{GraphRoot}/me?$select=displayName,mail,userPrincipalName,preferredLanguage",
            ProfileScopes,
            cancellationToken);

        return new CommunitySummary(
            DisplayName: root.GetStringOrDefault("displayName", "Unknown user") ?? "Unknown user",
            Mail: root.GetStringOrDefault("mail", "") ?? "",
            UserPrincipalName: root.GetStringOrDefault("userPrincipalName", "") ?? "",
            PreferredLanguage: root.GetStringOrDefault("preferredLanguage", "") ?? "");
    }

    public async Task<IReadOnlyList<WordDocumentSummary>> GetRecentWordDocumentsAsync(CancellationToken cancellationToken)
    {
        // drive/recent is deprecated, so use OneDrive search and then filter to
        // .docx files. The result is read-only metadata, not document contents.
        var values = await GetPagedValuesAsync(
            $"{GraphRoot}/me/drive/root/search(q='.docx')?$select=name,lastModifiedDateTime,webUrl,size,file&$top=25",
            FileScopes,
            cancellationToken,
            pageLimit: 1);

        return values
            .Where(item => item.GetStringOrDefault("name", "")?.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(item => item.GetDateTimeOffsetOrDefault("lastModifiedDateTime"))
            .Take(5)
            .Select(item => new WordDocumentSummary(
                Name: item.GetStringOrDefault("name", "Unnamed document") ?? "Unnamed document",
                LastModifiedAt: item.GetDateTimeOffsetOrDefault("lastModifiedDateTime"),
                WebUrl: item.GetStringOrDefault("webUrl", null),
                Size: item.GetLongOrNull("size")))
            .ToList();
    }

    public async Task<DashboardReport> BuildReportAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        IReadOnlyList<MailSummary> messages = Array.Empty<MailSummary>();
        IReadOnlyList<MeetingSummary> meetings = Array.Empty<MeetingSummary>();
        IReadOnlyList<WordDocumentSummary> wordDocuments = Array.Empty<WordDocumentSummary>();
        CommunitySummary? community = null;

        try { messages = await GetTodaysMessagesAsync(cancellationToken); }
        catch (Exception ex) when (!IsConsentChallenge(ex)) { errors.Add($"Outlook mail could not be loaded: {ex.Message}"); }

        try { meetings = await GetScheduledMeetingsAsync(cancellationToken); }
        catch (Exception ex) when (!IsConsentChallenge(ex)) { errors.Add($"Scheduled meetings could not be loaded: {ex.Message}"); }

        try { community = await GetCommunityAsync(cancellationToken); }
        catch (Exception ex) when (!IsConsentChallenge(ex)) { errors.Add($"Account/community profile could not be loaded: {ex.Message}"); }

        try { wordDocuments = await GetRecentWordDocumentsAsync(cancellationToken); }
        catch (Exception ex) when (!IsConsentChallenge(ex)) { errors.Add($"Recent Word documents could not be loaded: {ex.Message}"); }

        return new DashboardReport
        {
            GeneratedAt = DateTimeOffset.Now,
            Messages = messages,
            Meetings = meetings,
            Community = community,
            WordDocuments = wordDocuments,
            Errors = errors
        };
    }

    private async Task<JsonElement> GetObjectAsync(
        string url,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        using var response = await SendGraphGetAsync(url, scopes, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private async Task<IReadOnlyList<JsonElement>> GetPagedValuesAsync(
        string firstUrl,
        string[] scopes,
        CancellationToken cancellationToken,
        int pageLimit = 3)
    {
        var values = new List<JsonElement>();
        var nextUrl = firstUrl;
        var pagesRead = 0;

        while (!string.IsNullOrWhiteSpace(nextUrl) && pagesRead < pageLimit)
        {
            using var response = await SendGraphGetAsync(nextUrl, scopes, cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("value", out var valueArray))
            {
                foreach (var item in valueArray.EnumerateArray())
                {
                    values.Add(item.Clone());
                }
            }

            nextUrl = root.GetStringOrDefault("@odata.nextLink", null);
            pagesRead++;
        }

        return values;
    }

    private async Task<HttpResponseMessage> SendGraphGetAsync(
        string url,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        var accessToken = await tokenAcquisition.GetAccessTokenForUserAsync(scopes);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new InvalidOperationException(
                "Microsoft Graph rejected the request. Check app permissions and user consent.");
        }

        response.EnsureSuccessStatusCode();
        return response;
    }

    private string[] GetScopes(string sectionName, string[] fallback)
    {
        var scopes = configuration.GetSection(sectionName).Get<string[]>();
        return scopes is { Length: > 0 } ? scopes : fallback;
    }

    private static bool IsConsentChallenge(Exception exception)
    {
        // Microsoft.Identity.Web throws this when the user needs to grant a new
        // Graph permission. Let it bubble so AuthorizeForScopes can redirect the
        // browser to the Microsoft consent screen instead of showing a data error.
        return exception.GetType().Name == "MicrosoftIdentityWebChallengeUserException" ||
               exception.Message.Contains("IDW10502", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value
            : null;
    }

    public static string? GetStringOrDefault(this JsonElement element, string propertyName, string? defaultValue = "")
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : defaultValue;
    }

    public static bool GetBoolOrDefault(this JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    public static long? GetLongOrNull(this JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out var number)
            ? number
            : null;
    }

    public static DateTimeOffset GetDateTimeOffsetOrDefault(this JsonElement element, string propertyName)
    {
        var text = element.GetStringOrDefault(propertyName, null);
        return DateTimeOffset.TryParse(text, out var date) ? date : DateTimeOffset.MinValue;
    }

    public static DateTimeOffset GetGraphDateTimeOrDefault(this JsonElement element, string propertyName)
    {
        var dateTimeText = element.GetPropertyOrNull(propertyName)?.GetStringOrDefault("dateTime", null);
        return DateTimeOffset.TryParse(dateTimeText, out var date) ? date : DateTimeOffset.MinValue;
    }
}
