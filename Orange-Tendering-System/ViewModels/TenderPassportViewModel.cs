namespace Orange_Tendering_System.ViewModels;

// The Tender Passport: a tracked "package" whose stickers are real AuditLogs rows (Services/TenderPassportService.cs).
// Nothing here is invented: an empty value means the system has no record of it.

/// <summary>Sticker families. Each maps to one colour and icon in wwwroot/css/tender-passport.css.</summary>
public enum StickerKind { Created, Viewed, Modified, Reviewed, Returned, Corrected, Submitted, Approved, Rejected, Downloaded, Awarded }

/// <summary>The graphical package state, derived from the stored workflow state.</summary>
public enum PackageState { Open, Sealed, Inspection, Returned, Approved, Awarded, Archived }

/// <summary>One person as recorded at the time of the action.</summary>
public sealed record PassportPerson(string Name, string? Role, string? Unit);

/// <summary>One authorized audit event, ready to show on a sticker popover or in the activity trail.</summary>
public sealed record TrackingStickerViewModel
{
    public long AuditId { get; init; }               // 0 = taken from the entity row itself (e.g. Tenders.CreatedAt)
    public StickerKind Kind { get; init; }
    public string Action { get; init; } = "";        // raw AuditLogs.Action
    public string Label { get; init; } = "";         // readable action, e.g. "Weights approved"
    public string? ActorId { get; init; }
    public PassportPerson? Actor { get; init; }      // null = system action
    public DateTime AtUtc { get; init; }
    public string Reference { get; init; } = "";     // e.g. T-1003
    public int? Round { get; init; }
    public string? Version { get; init; }            // report / requirements / document version when the record names one
    public string? Document { get; init; }
    public string? Comment { get; init; }
    public string? PreviousStatus { get; init; }
    public string? NewStatus { get; init; }
    /// <summary>How many recorded events of this kind the sticker stands for (the sticker shows the latest).</summary>
    public int Count { get; init; } = 1;
}

public sealed class TenderPassportViewModel
{
    public string EntityName { get; init; } = "";
    public int EntityId { get; init; }
    public string Reference { get; init; } = "";
    public string Title { get; init; } = "";
    public PackageState State { get; init; }
    public string StateLabel { get; init; } = "";
    public string Stage { get; init; } = "";
    public int? Round { get; init; }
    public int? MaxRounds { get; init; }
    public string? Version { get; init; }
    public string Classification { get; init; } = "Orange Restricted";

    public PassportPerson? CreatedBy { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public TrackingStickerViewModel? LastModified { get; init; }
    public TrackingStickerViewModel? PreviousView { get; init; }
    public TrackingStickerViewModel? LastReviewed { get; init; }
    public TrackingStickerViewModel? LastCorrected { get; init; }
    public TrackingStickerViewModel? LatestAction { get; init; }
    public string? CurrentHolder { get; init; }
    public PassportPerson? CurrentViewer { get; init; }

    /// <summary>Stickers placed on the package (most relevant recent kinds); the rest are counted in <see cref="Overflow"/>.</summary>
    public IReadOnlyList<TrackingStickerViewModel> Stickers { get; init; } = [];
    public int Overflow { get; init; }
    public int TotalEvents { get; init; }
    /// <summary>Audit id of an action the current viewer completed moments ago: its sticker is "placed" with a short animation.</summary>
    public long? FreshAuditId { get; init; }
    public string? TrailUrl { get; init; }
}

/// <summary>The full activity trail (parcel-tracking style), loaded on demand.</summary>
public sealed class PassportTrailViewModel
{
    public string Reference { get; init; } = "";
    public string Title { get; init; } = "";
    public IReadOnlyList<TrackingStickerViewModel> Events { get; init; } = [];
    public bool Truncated { get; init; }
}
