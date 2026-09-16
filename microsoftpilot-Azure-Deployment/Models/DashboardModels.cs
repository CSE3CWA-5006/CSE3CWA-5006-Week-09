namespace MicrosoftPilot.Models;

// One Outlook message row shown in the page and exported to the DOCX report.
public sealed record MailSummary(
    string Subject,
    string From,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    string Importance,
    string? WebLink);

// One calendar item. The app reads events without applying a local date limit.
public sealed record MeetingSummary(
    string Subject,
    string Organizer,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    bool IsOnlineMeeting,
    string? WebLink);

// Basic signed-in account information. For personal Microsoft accounts there is no
// school tenant/community to read, so the report shows the Microsoft account
// profile that Graph returns with User.Read.
public sealed record CommunitySummary(
    string DisplayName,
    string Mail,
    string UserPrincipalName,
    string PreferredLanguage);

// One Word document from OneDrive, filtered to .docx and ordered by modified time.
public sealed record WordDocumentSummary(
    string Name,
    DateTimeOffset LastModifiedAt,
    string? WebUrl,
    long? Size);

// A single object shared by the Razor page and DOCX generator.
public sealed class DashboardReport
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<MailSummary> Messages { get; init; } = Array.Empty<MailSummary>();
    public IReadOnlyList<MeetingSummary> Meetings { get; init; } = Array.Empty<MeetingSummary>();
    public CommunitySummary? Community { get; init; }
    public IReadOnlyList<WordDocumentSummary> WordDocuments { get; init; } = Array.Empty<WordDocumentSummary>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public int UnreadCount => Messages.Count(message => !message.IsRead);
    public int HighImportanceCount => Messages.Count(message =>
        string.Equals(message.Importance, "high", StringComparison.OrdinalIgnoreCase));

    public int NormalImportanceCount => Messages.Count(message =>
        string.Equals(message.Importance, "normal", StringComparison.OrdinalIgnoreCase));
}
