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
/// Face sign-in with five-second capture. The browser turns a few camera frames into 128-number descriptors
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
    IOptions<BiometricOptions> options)
{
    public const string LoginPurpose = "face-login";
    public const string EnrollPurpose = "face-enroll";
    public const string DemoPurpose = "face-demo";

    private const int DescriptorLength = 128;
    private const int MinSamples = 3;
    private const int MaxSamples = 8;
    private const int SamplesStored = 5;

    private readonly IDataProtector protector = dataProtection.CreateProtector("Orange.Biometric.FaceTemplate.v1");

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

    /// <param name="user">The account named at sign-in; null when no such account exists (the result is the same generic failure).</param>
    public async Task<BiometricResult> VerifyLoginAsync(ApplicationUser? user, FaceSubmission submission)
    {
        const string failed = "Face verification failed.";

        var entry = challenges.Consume(submission.Token, LoginPurpose);
        if (entry is null)
            return BiometricResult.Fail("The request expired. Try again.");

        var problem = CheckSamples(submission);
        if (problem is not null || user is null)
            return BiometricResult.Fail(problem ?? failed);

        var rows = await db.FaceTemplates.Where(t => t.UserId == user.Id).ToListAsync();
        var templates = new List<float[]>();
        foreach (var row in rows)
        {
            try
            {
                templates.Add(FromBytes(protector.Unprotect(row.ProtectedTemplate)));
            }
            catch (CryptographicException)
            {
                // A template that can no longer be decrypted (keys changed) is ignored.
            }
        }

        if (templates.Count == 0 || !Match(submission.Descriptors, templates).Matched)
            return BiometricResult.Fail(failed);

        var now = DateTime.UtcNow;
        rows.ForEach(r => r.LastUsedAt = now);
        await db.SaveChangesAsync();
        return BiometricResult.Ok(user);
    }

    /// <summary>
    /// The face decision used by sign-in: each live sample is compared with its closest enrolled template; at least
    /// 70% of the samples (minimum 3) and the average distance must be within FaceMatchThreshold.
    /// </summary>
    public FaceMatch Match(IReadOnlyList<FaceDescriptorSample> live, IReadOnlyList<float[]> templates)
    {
        var best = live.Select(d => templates.Min(t => Distance(d.V, t))).ToList();
        var threshold = options.Value.FaceMatchThreshold;
        var matching = best.Count(d => d <= threshold);
        var required = Math.Max(MinSamples, (int)Math.Ceiling(best.Count * 0.7));
        var average = best.Count == 0 ? double.PositiveInfinity : best.Average();
        return new FaceMatch(best.Count > 0 && matching >= required && average <= threshold, best, average, matching, required, threshold);
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

        // One person in every sample ...
        if (largest > 0.5)
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
