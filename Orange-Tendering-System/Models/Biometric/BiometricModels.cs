using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Models.Biometric;

/// <summary>Settings for biometric sign-in (section "Biometric" in appsettings.json; every value has a default).</summary>
public class BiometricOptions
{
    /// <summary>WebAuthn relying-party id (the site name a passkey belongs to). Default: the host name of the request, for example "localhost".</summary>
    public string? RpId { get; set; }

    /// <summary>Origins a WebAuthn answer may come from. Default: the origin of the request.</summary>
    public string[]? Origins { get; set; }

    /// <summary>Largest allowed Euclidean distance between a live face descriptor and an enrolled one.</summary>
    public double FaceMatchThreshold { get; set; } = 0.5;

    /// <summary>How long a one-time face token stays valid.</summary>
    public int ChallengeSeconds { get; set; } = 90;

    /// <summary>How long a passkey challenge stays valid. Longer than the face one: using a phone (scan, unlock, confirm) takes a while.</summary>
    public int PasskeyChallengeSeconds { get; set; } = 300;

    /// <summary>How long the Face ID / Fingerprint identity step (SupplierPreAuth) stays open.</summary>
    public int PreAuthSeconds { get; set; } = 300;

    /// <summary>
    /// Owner decision 2026-11-15: an approved supplier may enter the supplier workspace with Email &amp; Password.
    /// Set to false to make Face ID or Fingerprint mandatory for the supplier workspace again.
    /// </summary>
    public bool SupplierPasswordOnly { get; set; } = true;

    /// <summary>
    /// Extra Data Protection application names whose face templates can still be read (they are re-encrypted with the
    /// current key scope on first use). Before 2026-11-15 the scope was the folder of whichever copy ran.
    /// </summary>
    public string[] LegacyKeyScopes { get; set; } = [];

    public int MaxFaceTemplatesPerUser { get; set; } = 10;
    public int MaxPasskeysPerUser { get; set; } = 10;

    /// <summary>
    /// The public HTTPS address of this site (for example the ngrok URL), used for the phone QR code when the page is
    /// opened on an address a phone cannot reach (http://localhost). Null: the phone flow works only when the page itself
    /// is opened over a public HTTPS address.
    /// </summary>
    public string? PublicOrigin { get; set; }

    /// <summary>Lifetime of a QR code / email link that registers a phone.</summary>
    public int PhoneEnrollSeconds { get; set; } = 600;

    /// <summary>Lifetime of a QR code that approves a computer's sign-in from the phone.</summary>
    public int PhoneSignInSeconds { get; set; } = 180;

    /// <summary>"Send setup link to my email" messages allowed per account inside the failure window.</summary>
    public int PhoneLinkEmailsPerWindow { get; set; } = 3;

    /// <summary>New phone QR codes allowed per account inside the failure window.</summary>
    public int PhoneLinksPerWindow { get; set; } = 10;

    /// <summary>Failed biometric attempts allowed per IP address / account inside the window.</summary>
    public int MaxFailuresPerWindow { get; set; } = 10;

    public int FailureWindowMinutes { get; set; } = 10;
}

/// <summary>The answer of a face or passkey check: success (with the account) or the error message.</summary>
public class BiometricResult
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }

    /// <summary>The account the check belongs to (sign-in flows).</summary>
    public ApplicationUser? User { get; set; }

    public static BiometricResult Fail(string error)
    {
        BiometricResult result = new BiometricResult();
        result.Succeeded = false;
        result.Error = error;
        return result;
    }

    public static BiometricResult Ok(ApplicationUser? user)
    {
        BiometricResult result = new BiometricResult();
        result.Succeeded = true;
        result.User = user;
        return result;
    }
}

// ---------- Fingerprint / passkey (WebAuthn). The JSON bodies sent by wwwroot/js/biometric.js. ----------

public class PasskeyRegisterRequest
{
    public string? CredentialJson { get; set; }
    public string? Label { get; set; }
}

/// <summary>Every enrollment starts with the current password, so a stolen session cannot add its own face or device.</summary>
public class EnrollStartRequest
{
    public string? Password { get; set; }
}

public class PasskeyLoginRequest
{
    public string? CredentialJson { get; set; }
    public string? ReturnUrl { get; set; }
}

// ---------- Phone link (QR code / email link opened on a smartphone). Sent by wwwroot/js/phone-passkey.js. ----------

public class PhoneLinkRequest
{
    /// <summary>The one-time token from the QR code or email link.</summary>
    public string? Token { get; set; }

    /// <summary>Sign-in only: the two-digit number shown on the computer.</summary>
    public string? Code { get; set; }

    public string? CredentialJson { get; set; }
}

public class PhoneSignInCompleteRequest
{
    public string? ReturnUrl { get; set; }
}

// ---------- Face recognition ----------

/// <summary>One face sample from the browser.</summary>
public class FaceDescriptorSample
{
    /// <summary>Milliseconds since the capture started.</summary>
    public double T { get; set; }

    /// <summary>The 128 numbers (descriptor) computed by face-api.js.</summary>
    public float[] V { get; set; } = [];
}

/// <summary>The JSON body of /Biometrics/FaceEnroll, /Biometrics/FaceLogin and the face demo.</summary>
public class FaceSubmission
{
    /// <summary>The one-time token from the challenge request.</summary>
    public string Token { get; set; } = string.Empty;

    public List<FaceDescriptorSample> Descriptors { get; set; } = [];

    /// <summary>Enrollment only: replace the stored samples instead of adding to them.</summary>
    public bool Replace { get; set; }

    public string? ReturnUrl { get; set; }
}

/// <summary>The details of one face comparison (FaceService.Match). The demo page shows these numbers.</summary>
public class FaceMatch
{
    public bool Matched { get; set; }

    /// <summary>For each live sample: the distance to its closest enrolled template.</summary>
    public List<double> BestDistances { get; set; } = new List<double>();

    /// <summary>The distance of the averaged live descriptor.</summary>
    public double AverageDistance { get; set; }

    public int MatchingSamples { get; set; }
    public int RequiredSamples { get; set; }
    public double Threshold { get; set; }
}

/// <summary>A one-time token for one face attempt.</summary>
public class FaceChallengeResponse
{
    public string Token { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}

// ---------- Pages ----------

public class FaceDemoViewModel
{
    /// <summary>Samples of the current test face kept in memory (0 = none registered).</summary>
    public int TestFaceSamples { get; set; }

    public double Threshold { get; set; }
}

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
}
