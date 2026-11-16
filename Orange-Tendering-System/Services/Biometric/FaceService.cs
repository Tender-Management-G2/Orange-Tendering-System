using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// Face sign-in with an eight-second capture. The browser turns a few camera frames into 128-number descriptors
/// (face-api.js); the server issues a one-time token per attempt (so a captured request cannot be replayed),
/// checks the samples are valid and from one face, and compares them with the templates enrolled for the account.
///
/// Templates are encrypted with ASP.NET Core Data Protection in the FaceTemplates table; no image is stored
/// or uploaded. Limitation (owner decision, academic demo): there is NO liveness or spoof protection, so a
/// photo or video of the person held up to the camera can pass.
/// </summary>
public class FaceService(
    ApplicationDbContext db,
    ChallengeStore challenges,
    IDataProtectionProvider dataProtection,
    IOptions<BiometricOptions> options,
    ILogger<FaceService> logger,
    IHostEnvironment environment)
{
    private const string TemplatePurpose = "Orange.Biometric.FaceTemplate.v1";

    /// <summary>Shown after the password step when no stored face can be read under any known key scope.</summary>
    public const string ReEnrollRequired = "Your saved face record could not be opened on this server, so it could not be compared. This is not a face mismatch. Use Email & Password or Fingerprint to sign in, then enroll your face again.";

    public const string LoginPurpose = "face-login";
    public const string EnrollPurpose = "face-enroll";
    public const string DemoPurpose = "face-demo";

    private const int DescriptorLength = 128;
    private const int MinSamples = 3;
    private const int MaxSamples = 14; // the browser sends one sample every 0.7 s over 7 s
    private const int SamplesStored = 8; // more enrolled views of the face: fewer false rejections

    private readonly IDataProtector protector = dataProtection.CreateProtector(TemplatePurpose);

    public FaceChallengeResponse CreateChallenge(string purpose, string? userId) => new()
    {
        Token = challenges.Create(purpose, RandomNumberGenerator.GetBytes(16), userId),
        ExpiresInSeconds = options.Value.ChallengeSeconds
    };

    public Task<int> CountTemplatesAsync(string userId) => db.FaceTemplates.AsNoTracking().CountAsync(t => t.UserId == userId);

    public Task<int> RemoveTemplatesAsync(string userId) => db.FaceTemplates.Where(t => t.UserId == userId).ExecuteDeleteAsync();

    public async Task<BiometricResult> EnrollAsync(ApplicationUser user, FaceSubmission submission)
    {
        var entry = challenges.Consume(submission.Token, EnrollPurpose);
        if (entry is null || entry.UserId != user.Id)
            return BiometricResult.Fail("The request expired. Try again.");

        var problem = CheckSamples(submission);
        if (problem is not null)
            return BiometricResult.Fail(problem);

        var existing = submission.Replace ? 0 : await CountTemplatesAsync(user.Id);
        var samples = Spread(submission.Descriptors, SamplesStored);
        if (existing + samples.Count > options.Value.MaxFaceTemplatesPerUser)
            return BiometricResult.Fail("You reached the maximum number of stored face samples. Replace them instead.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (submission.Replace)
            await RemoveTemplatesAsync(user.Id);

        var now = DateTime.UtcNow;
        db.FaceTemplates.AddRange(samples.Select(s => new FaceTemplate
        {
            UserId = user.Id, ProtectedTemplate = protector.Protect(ToBytes(s.V)), CreatedAt = now
        }));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return BiometricResult.Ok(user);
    }

    /// <param name="user">The account that passed the password step (SupplierPreAuth); null gives the same generic failure.</param>
    public async Task<BiometricResult> VerifyLoginAsync(ApplicationUser? user, FaceSubmission submission)
    {
        const string failed = "Face verification failed.";

        var entry = challenges.Consume(submission.Token, LoginPurpose);
        if (entry is null)
            return BiometricResult.Fail("The request expired. Try again.");

        var problem = CheckSamples(submission);
        // The token was issued to the account that passed the password step; it cannot be spent on another one.
        if (problem is not null || user is null || entry.UserId != user.Id)
            return BiometricResult.Fail(problem ?? failed);

        var rows = await db.FaceTemplates.Where(t => t.UserId == user.Id).ToListAsync();
        var templates = new List<float[]>();
        var migrated = 0;
        foreach (var row in rows)
        {
            var bytes = Read(row.ProtectedTemplate);
            if (bytes is null)
                continue;
            if (bytes.Value.Legacy)
            {
                // Enrolled under the old folder-based key scope: re-encrypt it under the current one, once.
                row.ProtectedTemplate = protector.Protect(bytes.Value.Data);
                migrated++;
            }
            templates.Add(FromBytes(bytes.Value.Data));
        }
        if (migrated > 0)
        {
            logger.LogInformation("Re-encrypted {Count} face template(s) of user {UserId} under the current key scope.", migrated, user.Id);
            await db.SaveChangesAsync();
        }

        if (rows.Count > 0 && templates.Count == 0)
        {
            // Not a mismatch: nothing to compare with. Say so (the password was already correct) instead of failing forever.
            logger.LogWarning("Face templates for user {UserId} could not be decrypted under any known key scope.", user.Id);
            return BiometricResult.Fail(ReEnrollRequired);
        }

        if (templates.Count == 0 || !Match(submission.Descriptors, templates).Matched)
            return BiometricResult.Fail(failed);

        var now = DateTime.UtcNow;
        rows.ForEach(r => r.LastUsedAt = now);
        await db.SaveChangesAsync();
        return BiometricResult.Ok(user);
    }

    /// <summary>
    /// The face decision used by sign-in. The live samples are averaged into one descriptor (single frames are noisy:
    /// blur, a blink, a half-turned head); that average must be within FaceMatchThreshold of the closest enrolled
    /// template, and at least 60% of the individual samples (minimum 3) must be within it too. The threshold itself is
    /// unchanged, so the decision is steadier for the enrolled person without being easier for someone else.
    /// AverageDistance reports the averaged descriptor's distance.
    /// </summary>
    public FaceMatch Match(IReadOnlyList<FaceDescriptorSample> live, IReadOnlyList<float[]> templates)
    {
        var best = live.Select(d => templates.Min(t => Distance(d.V, t))).ToList();
        var threshold = options.Value.FaceMatchThreshold;
        var matching = best.Count(d => d <= threshold);
        var required = Math.Max(MinSamples, (int)Math.Ceiling(best.Count * 0.6));
        var centroid = best.Count == 0 ? double.PositiveInfinity : templates.Min(t => Distance(Centroid(live), t));
        return new FaceMatch(best.Count > 0 && matching >= required && centroid <= threshold, best, centroid, matching, required, threshold);
    }

    private static float[] Centroid(IReadOnlyList<FaceDescriptorSample> live)
    {
        var mean = new float[DescriptorLength];
        foreach (var sample in live)
            for (var i = 0; i < DescriptorLength; i++)
                mean[i] += sample.V[i] / live.Count;
        return mean;
    }

    // ---- Reading templates, including ones encrypted under an earlier key scope ----

    /// <summary>The template bytes, flagged when they could only be read with a legacy key scope; null when unreadable.</summary>
    private (byte[] Data, bool Legacy)? Read(byte[] protectedTemplate)
    {
        try
        {
            return (protector.Unprotect(protectedTemplate), false);
        }
        catch (CryptographicException)
        {
        }
        foreach (var legacy in LegacyProtectors())
        {
            try
            {
                return (legacy.Unprotect(protectedTemplate), true);
            }
            catch (CryptographicException)
            {
            }
        }
        return null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IDataProtector[]> LegacyCache = new();

    /// <summary>
    /// Until 2026-11-15 AddDataProtection had no application name, so its scope was the content root folder of the
    /// running copy. The key files are the same (per Windows user, %LOCALAPPDATA%\ASP.NET\DataProtection-Keys); only
    /// the scope differs, so those templates are recoverable by opening the same key ring under the old scope.
    /// </summary>
    private IDataProtector[] LegacyProtectors()
    {
        var root = environment.ContentRootPath;
        return LegacyCache.GetOrAdd(root, _ =>
        {
            var keys = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASP.NET", "DataProtection-Keys"));
            if (!keys.Exists)
                return [];
            var folders = new List<string> { root };
            if (environment.IsDevelopment())
            {
                // Sibling checkouts of this repository (Visual Studio folder, worktrees) used their own folder as scope.
                var repositories = Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.Parent;
                if (repositories?.Exists == true)
                    folders.AddRange(repositories.GetDirectories()
                        .Select(d => Path.Combine(d.FullName, "Orange-Tendering-System"))
                        .Where(Directory.Exists));
            }
            var scopes = options.Value.LegacyKeyScopes
                .Concat(folders.SelectMany(f => new[]
                {
                    f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar
                }))
                .Distinct(StringComparer.Ordinal);
            return scopes
                .Select(scope => DataProtectionProvider.Create(keys, b => b.SetApplicationName(scope)).CreateProtector(TemplatePurpose))
                .ToArray();
        });
    }

    // ---- Development-only /face-demo (Controllers/FaceDemoController.cs) ----
    // Same one-time token, sample checks and matching as the real flow; the templates are kept by the caller in memory,
    // never written to FaceTemplates, and no account is involved.

    /// <summary>Checks a demo enrollment and returns the samples to keep, or the problem with the capture.</summary>
    public (string? Problem, List<float[]> Templates) CheckDemoEnrollment(FaceSubmission submission, string demoId)
    {
        var entry = challenges.Consume(submission.Token, DemoPurpose);
        if (entry is null || entry.UserId != demoId)
            return ("The request expired. Try again.", []);
        var problem = CheckSamples(submission);
        return problem is not null
            ? (problem, [])
            : (null, Spread(submission.Descriptors, SamplesStored).Select(s => s.V).ToList());
    }

    /// <summary>Checks a demo verification exactly like sign-in and returns the capture problem or the match details.</summary>
    public (string? Problem, FaceMatch? Match) CheckDemoVerification(FaceSubmission submission, string demoId, IReadOnlyList<float[]> templates)
    {
        var entry = challenges.Consume(submission.Token, DemoPurpose);
        if (entry is null || entry.UserId != demoId)
            return ("The request expired. Try again.", null);
        var problem = CheckSamples(submission);
        return problem is not null ? (problem, null) : (null, Match(submission.Descriptors, templates));
    }

    // No liveness check (removed by owner decision). The samples must be well-formed descriptors, all of one face,
    // taken from different camera frames. Returns the problem, or null when they are acceptable.
    private static string? CheckSamples(FaceSubmission submission)
    {
        var samples = submission.Descriptors;
        if (samples.Count < MinSamples || samples.Count > MaxSamples)
            return "Not enough face samples were captured. Try again.";

        foreach (var sample in samples)
        {
            if (sample.V.Length != DescriptorLength || sample.V.Any(v => !float.IsFinite(v) || Math.Abs(v) > 3) ||
                !double.IsFinite(sample.T) || sample.T < 0)
                return "The face samples are not valid.";
        }

        if (samples[^1].T - samples[0].T < 5000 ||
            samples.Zip(samples.Skip(1)).Any(pair => pair.Second.T <= pair.First.T))
            return "Look at the camera continuously for 5 seconds, then try again.";

        double largest = 0;
        for (var i = 0; i < samples.Count; i++)
            for (var j = i + 1; j < samples.Count; j++)
                largest = Math.Max(largest, Distance(samples[i].V, samples[j].V));

        // One person in every sample (a little slack for natural movement over eight seconds) ...
        if (largest > 0.55)
            return "The captured samples are not all the same face. Look at the camera alone and try again.";

        // ... but never a perfect copy: a real camera always adds a little noise, so identical
        // samples mean one value was repeated (a replayed or forged request).
        if (largest == 0)
            return "The face samples are not valid.";

        return null;
    }

    private static List<FaceDescriptorSample> Spread(List<FaceDescriptorSample> all, int count) =>
        all.Count <= count
            ? all.ToList()
            : Enumerable.Range(0, count).Select(i => all[i * (all.Count - 1) / (count - 1)]).ToList();

    private static double Distance(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }

        return Math.Sqrt(sum);
    }

    private static byte[] ToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), values[i]);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * sizeof(float)));
        return values;
    }
}
