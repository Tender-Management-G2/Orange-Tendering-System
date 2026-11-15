using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Models.Biometric;

/// <summary>Settings for biometric sign-in (section "Biometric" in configuration; every value has a default).</summary>
public class BiometricOptions
{
    /// <summary>WebAuthn relying-party id. Default: the host name of the request (for example "localhost").</summary>
    public string? RpId { get; set; }

    /// <summary>Origins a WebAuthn response may come from. Default: the origin of the request.</summary>
    public string[]? Origins { get; set; }

    public string RpName { get; set; } = "Sourcing & Tender Management System";

    /// <summary>Largest allowed Euclidean distance between a live face descriptor and an enrolled one.</summary>
    public double FaceMatchThreshold { get; set; } = 0.5;

    /// <summary>How long a one-time face challenge stays valid.</summary>
    public int ChallengeSeconds { get; set; } = 90;

    /// <summary>
    /// How long a one-time fingerprint (passkey) challenge stays valid. Longer than the face one,
    /// because using a phone through the QR code (scan, unlock, confirm) takes a while.
    /// </summary>
    public int PasskeyChallengeSeconds { get; set; } = 300;

    /// <summary>Legacy setting, not used: face sign-in is email + live face (suppliers use the password only for setup).</summary>
    public bool FaceLoginRequiresPassword { get; set; } = true;

    public int MaxFaceTemplatesPerUser { get; set; } = 10;
    public int MaxPasskeysPerUser { get; set; } = 10;

    /// <summary>Failed biometric attempts allowed per IP address / account name inside the window.</summary>
    public int MaxFailuresPerWindow { get; set; } = 10;

    public int FailureWindowMinutes { get; set; } = 10;
}

/// <summary>Relying-party details for one request.</summary>
public record RpInfo(string RpId, string[] Origins, string RpName);

public class BiometricResult
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }

    /// <summary>The account the verification belongs to (sign-in flows).</summary>
    public ApplicationUser? User { get; set; }

    public static BiometricResult Fail(string error) => new() { Error = error };
    public static BiometricResult Ok(ApplicationUser? user = null) => new() { Succeeded = true, User = user };
}

// ---------- Fingerprint / device passkey (WebAuthn) ----------

public class PasskeyRegisterRequest
{
    public string? CredentialJson { get; set; }
    public string? Label { get; set; }
}

/// <summary>Enrolment starts with a password re-check, so a hijacked session cannot add its own biometrics.</summary>
public class EnrollStartRequest
{
    public string? Password { get; set; }
}

public class PasskeyLoginRequest
{
    public string? CredentialJson { get; set; }
    public string? ReturnUrl { get; set; }
}

// ---------- Face recognition (instant capture, no liveness) ----------

public class FaceDescriptorSample
{
    /// <summary>Milliseconds since capture started.</summary>
    public double T { get; set; }

    /// <summary>The 128-number face descriptor produced in the browser.</summary>
    public float[] V { get; set; } = [];
}

public class FaceSubmission
{
    public string Token { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>Legacy field, ignored (the enrollment password is checked when the challenge is issued).</summary>
    public string? Password { get; set; }

    public List<FaceDescriptorSample> Descriptors { get; set; } = [];

    /// <summary>Enrolment only: replace the templates already stored instead of adding to them.</summary>
    public bool Replace { get; set; }

    public string? ReturnUrl { get; set; }
}

/// <summary>
/// Result of comparing live face samples with enrolled templates (FaceService.Match). Used by sign-in and,
/// with its numbers shown, by the development-only /face-demo page.
/// </summary>
/// <param name="BestDistances">For each live sample, the distance to its closest enrolled template.</param>
public record FaceMatch(bool Matched, IReadOnlyList<double> BestDistances, double AverageDistance,
    int MatchingSamples, int RequiredSamples, double Threshold);

/// <summary>A one-time token for one face attempt, so a captured submission cannot be replayed.</summary>
public class FaceChallengeResponse
{
    public string Token { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

// ---------- Development-only face demo (/face-demo) ----------

public class FaceDemoViewModel
{
    /// <summary>Number of samples of the current test face kept in memory (0 = none registered).</summary>
    public int TestFaceSamples { get; set; }

    public double Threshold { get; set; }
}

// ---------- Manage page ----------

public class BiometricManageViewModel
{
    public List<PasskeyRow> Passkeys { get; set; } = [];
    public int FaceTemplateCount { get; set; }
}

public class PasskeyRow
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
