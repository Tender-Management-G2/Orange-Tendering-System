namespace Orange_Tendering_System.ViewModels;

// Public editorial content only. This model has no relationship to Identity users.
public sealed class TeamMemberViewModel
{
    public string FullName { get; init; } = "Team member — placeholder";
    public string Role { get; init; } = "Actual project role to be provided";
    public string Contributions { get; init; } = "Verified contributions to be provided.";
    public string? Image { get; init; }
    public string? GitHub { get; init; }
    public string? LinkedIn { get; init; }
    public bool Placeholder { get; init; } = true;

    public static string? ProfileUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url.AbsoluteUri : null;
}
