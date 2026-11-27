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
/// Face enrollment and face sign-in.
///
/// The browser (face-api.js) turns camera frames into "descriptors": 128 numbers that describe a face. Only these
/// numbers are sent; the camera image never leaves the device. For every attempt the server first issues a one-time
/// token (so a recorded request cannot be replayed), then checks that the samples are valid and all of one face, and
/// compares them with the templates stored for the account.
///
/// Templates are encrypted with ASP.NET Core Data Protection in the FaceTemplates table.
/// Limitation (owner decision, academic project): there is NO liveness or spoof protection, so a photo or video of
/// the person held up to the camera can pass.
/// </summary>
public class FaceService
{
    // The Data Protection "purpose" of face templates. Changing it would make every stored face unreadable.
    private const string TemplatePurpose = "Orange.Biometric.FaceTemplate.v1";

    /// <summary>Shown when no stored face of the account can be decrypted on this server.</summary>
    public const string ReEnrollRequired = "Your saved face record could not be opened on this server, so it could not be compared. This is not a face mismatch. Use Email & Password or Fingerprint to sign in, then enroll your face again.";

    public const string LoginPurpose = "face-login";
    public const string EnrollPurpose = "face-enroll";
    public const string DemoPurpose = "face-demo";

    private const int DescriptorLength = 128;
    private const int MinSamples = 3;
    private const int MaxSamples = 14;     // the browser sends one sample every 0.7 s for 7 s
    private const int SamplesStored = 8;   // more enrolled views of the face give fewer false rejections

    private readonly ApplicationDbContext db;
    private readonly ChallengeStore challenges;
    private readonly BiometricOptions options;
    private readonly ILogger<FaceService> logger;
    private readonly IHostEnvironment environment;
    private readonly IDataProtector protector;

    public FaceService(
        ApplicationDbContext db,
        ChallengeStore challenges,
        IDataProtectionProvider dataProtection,
        IOptions<BiometricOptions> options,
        ILogger<FaceService> logger,
        IHostEnvironment environment)
    {
        this.db = db;
        this.challenges = challenges;
        this.options = options.Value;
        this.logger = logger;
        this.environment = environment;
        this.protector = dataProtection.CreateProtector(TemplatePurpose);
    }

    /// <summary>A one-time token for one face attempt of this account (or demo browser).</summary>
    public FaceChallengeResponse CreateChallenge(string purpose, string? userId)
    {
        FaceChallengeResponse response = new FaceChallengeResponse();
        response.Token = challenges.Create(purpose, userId, options.ChallengeSeconds);
        response.ExpiresInSeconds = options.ChallengeSeconds;
        return response;
    }

    public async Task<int> CountTemplatesAsync(string userId)
    {
        return await db.FaceTemplates.AsNoTracking().CountAsync(t => t.UserId == userId);
    }

    public async Task<int> RemoveTemplatesAsync(string userId)
    {
        return await db.FaceTemplates.Where(t => t.UserId == userId).ExecuteDeleteAsync();
    }

    // ---------------------------------------------------------------- Enrollment

    public async Task<BiometricResult> EnrollAsync(ApplicationUser user, FaceSubmission submission)
    {
        // 1. The token must be a live enrollment token issued to this same account.
        ChallengeEntry? entry = challenges.Consume(submission.Token, EnrollPurpose);
        if (entry == null || entry.UserId != user.Id)
        {
            return BiometricResult.Fail("The request expired. Try again.");
        }

        // 2. The samples must be valid and all of one face.
        string? problem = CheckSamples(submission.Descriptors);
        if (problem != null)
        {
            return BiometricResult.Fail(problem);
        }

        // 3. Respect the maximum number of stored samples.
        int existing = 0;
        if (!submission.Replace)
        {
            existing = await CountTemplatesAsync(user.Id);
        }
        List<FaceDescriptorSample> samples = PickSpreadSamples(submission.Descriptors, SamplesStored);
        if (existing + samples.Count > options.MaxFaceTemplatesPerUser)
        {
            return BiometricResult.Fail("You reached the maximum number of stored face samples. Replace them instead.");
        }

        // 4. Save: one encrypted FaceTemplates row per sample, all in one transaction.
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (submission.Replace)
        {
            await RemoveTemplatesAsync(user.Id);
        }

        DateTime now = DateTime.UtcNow;
        foreach (FaceDescriptorSample sample in samples)
        {
            FaceTemplate template = new FaceTemplate();
            template.UserId = user.Id;
            template.ProtectedTemplate = protector.Protect(ToBytes(sample.V));
            template.CreatedAt = now;
            db.FaceTemplates.Add(template);
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return BiometricResult.Ok(user);
    }

    // ---------------------------------------------------------------- Sign-in

    /// <param name="user">The account of the identity step (SupplierPreAuth). Null gives the same generic failure.</param>
    public async Task<BiometricResult> VerifyLoginAsync(ApplicationUser? user, FaceSubmission submission)
    {
        const string failed = "Face verification failed.";

        // 1. The token must be a live sign-in token...
        ChallengeEntry? entry = challenges.Consume(submission.Token, LoginPurpose);
        if (entry == null)
        {
            return BiometricResult.Fail("The request expired. Try again.");
        }

        // 2. ...with valid samples, issued to this same account (a token cannot be spent on another account).
        string? problem = CheckSamples(submission.Descriptors);
        if (problem != null)
        {
            return BiometricResult.Fail(problem);
        }
        if (user == null || entry.UserId != user.Id)
        {
            return BiometricResult.Fail(failed);
        }

        // 3. Decrypt the stored templates of the account.
        List<FaceTemplate> rows = await db.FaceTemplates.Where(t => t.UserId == user.Id).ToListAsync();
        List<float[]> templates = new List<float[]>();
        int reEncrypted = 0;
        foreach (FaceTemplate row in rows)
        {
            byte[]? bytes = UnprotectWithCurrentKey(row.ProtectedTemplate);
            if (bytes == null)
            {
                // Enrolled under the old key scope (before 2026-11-15): read it once and re-encrypt it with the current one.
                bytes = UnprotectWithOldKeys(row.ProtectedTemplate);
                if (bytes != null)
                {
                    row.ProtectedTemplate = protector.Protect(bytes);
                    reEncrypted++;
                }
            }
            if (bytes != null)
            {
                templates.Add(FromBytes(bytes));
            }
        }
        if (reEncrypted > 0)
        {
            logger.LogInformation("Re-encrypted {Count} face template(s) of user {UserId} under the current key scope.", reEncrypted, user.Id);
            await db.SaveChangesAsync();
        }

        if (rows.Count > 0 && templates.Count == 0)
        {
            // Not a mismatch: there is nothing readable to compare with. Say so instead of failing forever.
            logger.LogWarning("Face templates for user {UserId} could not be decrypted under any known key scope.", user.Id);
            return BiometricResult.Fail(ReEnrollRequired);
        }

        // 4. Compare.
        if (templates.Count == 0)
        {
            return BiometricResult.Fail(failed);
        }
        FaceMatch match = Match(submission.Descriptors, templates);
        if (!match.Matched)
        {
            return BiometricResult.Fail(failed);
        }

        // 5. Remember when the templates were last used.
        DateTime now = DateTime.UtcNow;
        foreach (FaceTemplate row in rows)
        {
            row.LastUsedAt = now;
        }
        await db.SaveChangesAsync();
        return BiometricResult.Ok(user);
    }

    /// <summary>
    /// The face decision. Three rules must all pass:
    ///   1. For each live sample, find its distance to the closest enrolled template ("best distance").
    ///   2. At least 60% of the samples (and at least 3) must have a best distance within FaceMatchThreshold.
    ///   3. The average of all live samples (single frames are noisy) must also be within the threshold of the closest template.
    /// A smaller distance means more similar faces.
    /// </summary>
    public FaceMatch Match(List<FaceDescriptorSample> live, List<float[]> templates)
    {
        double threshold = options.FaceMatchThreshold;

        // Rule 1: the best distance of every live sample.
        List<double> bestDistances = new List<double>();
        foreach (FaceDescriptorSample sample in live)
        {
            bestDistances.Add(ClosestDistance(sample.V, templates));
        }

        // Rule 2: how many samples are close enough, and how many are required.
        int matchingSamples = 0;
        foreach (double distance in bestDistances)
        {
            if (distance <= threshold)
            {
                matchingSamples++;
            }
        }
        int requiredSamples = (int)Math.Ceiling(bestDistances.Count * 0.6);
        if (requiredSamples < MinSamples)
        {
            requiredSamples = MinSamples;
        }

        // Rule 3: the distance of the averaged descriptor.
        double averageDistance = double.PositiveInfinity;
        if (bestDistances.Count > 0)
        {
            averageDistance = ClosestDistance(AverageDescriptor(live), templates);
        }

        FaceMatch match = new FaceMatch();
        match.Matched = bestDistances.Count > 0 && matchingSamples >= requiredSamples && averageDistance <= threshold;
        match.BestDistances = bestDistances;
        match.AverageDistance = averageDistance;
        match.MatchingSamples = matchingSamples;
        match.RequiredSamples = requiredSamples;
        match.Threshold = threshold;
        return match;
    }

    // ---------------------------------------------------------------- Development-only /face-demo

    /// <summary>
    /// For Controllers/FaceDemoController.cs: the same one-time token and sample checks as the real flow.
    /// Returns the problem with the capture, or null when it is acceptable. No account and no FaceTemplates row is involved.
    /// </summary>
    public string? CheckDemoSubmission(FaceSubmission submission, string demoId)
    {
        ChallengeEntry? entry = challenges.Consume(submission.Token, DemoPurpose);
        if (entry == null || entry.UserId != demoId)
        {
            return "The request expired. Try again.";
        }
        return CheckSamples(submission.Descriptors);
    }

    /// <summary>For the demo: the samples that enrollment would store, as plain number arrays.</summary>
    public List<float[]> DemoTemplates(List<FaceDescriptorSample> samples)
    {
        List<float[]> templates = new List<float[]>();
        foreach (FaceDescriptorSample sample in PickSpreadSamples(samples, SamplesStored))
        {
            templates.Add(sample.V);
        }
        return templates;
    }

    // ---------------------------------------------------------------- Checks and math

    // There is no liveness check (owner decision). The samples must be well-formed descriptors, all of one face,
    // taken from different camera frames over at least 5 seconds. Returns the problem, or null when they are acceptable.
    private static string? CheckSamples(List<FaceDescriptorSample> samples)
    {
        if (samples.Count < MinSamples || samples.Count > MaxSamples)
        {
            return "Not enough face samples were captured. Try again.";
        }

        foreach (FaceDescriptorSample sample in samples)
        {
            if (sample.V.Length != DescriptorLength)
            {
                return "The face samples are not valid.";
            }
            foreach (float value in sample.V)
            {
                if (!float.IsFinite(value) || Math.Abs(value) > 3)
                {
                    return "The face samples are not valid.";
                }
            }
            if (!double.IsFinite(sample.T) || sample.T < 0)
            {
                return "The face samples are not valid.";
            }
        }

        // T is the time of the sample in milliseconds. The capture must last 5 seconds and the times must go forward.
        double captureLength = samples[samples.Count - 1].T - samples[0].T;
        if (captureLength < 5000)
        {
            return "Look at the camera continuously for 5 seconds, then try again.";
        }
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].T <= samples[i - 1].T)
            {
                return "Look at the camera continuously for 5 seconds, then try again.";
            }
        }

        // The largest distance between any two samples.
        double largest = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            for (int j = i + 1; j < samples.Count; j++)
            {
                double distance = Distance(samples[i].V, samples[j].V);
                if (distance > largest)
                {
                    largest = distance;
                }
            }
        }

        // One person in every sample (a little room for natural movement)...
        if (largest > 0.55)
        {
            return "The captured samples are not all the same face. Look at the camera alone and try again.";
        }

        // ...but never perfect copies: a real camera always adds a little noise, so identical samples mean the same
        // numbers were repeated (a replayed or forged request).
        if (largest == 0)
        {
            return "The face samples are not valid.";
        }

        return null;
    }

    // Picks "count" samples spread evenly over the capture (first, ..., last), or all of them when there are fewer.
    private static List<FaceDescriptorSample> PickSpreadSamples(List<FaceDescriptorSample> all, int count)
    {
        List<FaceDescriptorSample> picked = new List<FaceDescriptorSample>();
        if (all.Count <= count)
        {
            picked.AddRange(all);
            return picked;
        }

        for (int i = 0; i < count; i++)
        {
            int index = i * (all.Count - 1) / (count - 1);
            picked.Add(all[index]);
        }
        return picked;
    }

    // The smallest distance between one descriptor and any of the templates.
    private static double ClosestDistance(float[] descriptor, List<float[]> templates)
    {
        double closest = double.PositiveInfinity;
        foreach (float[] template in templates)
        {
            double distance = Distance(descriptor, template);
            if (distance < closest)
            {
                closest = distance;
            }
        }
        return closest;
    }

    // The average of all live descriptors, number by number.
    private static float[] AverageDescriptor(List<FaceDescriptorSample> live)
    {
        float[] average = new float[DescriptorLength];
        foreach (FaceDescriptorSample sample in live)
        {
            for (int i = 0; i < DescriptorLength; i++)
            {
                average[i] += sample.V[i] / live.Count;
            }
        }
        return average;
    }

    // Euclidean distance: the square root of the sum of squared differences.
    private static double Distance(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            float difference = a[i] - b[i];
            sum += difference * difference;
        }
        return Math.Sqrt(sum);
    }

    // 128 floats become 512 bytes (4 bytes each, little-endian), the format stored encrypted in FaceTemplates.
    private static byte[] ToBytes(float[] values)
    {
        byte[] bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        }
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        float[] values = new float[bytes.Length / 4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }
        return values;
    }

    // ---------------------------------------------------------------- Reading templates enrolled under an old key scope

    private byte[]? UnprotectWithCurrentKey(byte[] protectedTemplate)
    {
        try
        {
            return protector.Unprotect(protectedTemplate);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private byte[]? UnprotectWithOldKeys(byte[] protectedTemplate)
    {
        foreach (IDataProtector oldProtector in OldProtectors())
        {
            try
            {
                return oldProtector.Unprotect(protectedTemplate);
            }
            catch (CryptographicException)
            {
                // Not this one: try the next old scope.
            }
        }
        return null;
    }

    // Built once per content root folder and then reused.
    private static readonly Dictionary<string, List<IDataProtector>> oldProtectorsByRoot = new Dictionary<string, List<IDataProtector>>();
    private static readonly object oldProtectorsLock = new object();

    /// <summary>
    /// Until 2026-11-15 Data Protection had no fixed application name, so the key "scope" was the folder of the running
    /// copy. The key files are the same (per Windows user, %LOCALAPPDATA%\ASP.NET\DataProtection-Keys); only the scope
    /// differs. So faces enrolled then can still be read by opening the same keys under the old scope names.
    /// </summary>
    private List<IDataProtector> OldProtectors()
    {
        string root = environment.ContentRootPath;
        lock (oldProtectorsLock)
        {
            if (oldProtectorsByRoot.ContainsKey(root))
            {
                return oldProtectorsByRoot[root];
            }

            List<IDataProtector> protectors = new List<IDataProtector>();
            string keyFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASP.NET", "DataProtection-Keys");
            DirectoryInfo keyFolder = new DirectoryInfo(keyFolderPath);
            if (keyFolder.Exists)
            {
                // The folders that may have been used as a scope: this copy, and (in Development) sibling checkouts.
                List<string> folders = new List<string>();
                folders.Add(root);
                if (environment.IsDevelopment())
                {
                    DirectoryInfo? projectFolder = Directory.GetParent(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    DirectoryInfo? repositoriesFolder = null;
                    if (projectFolder != null)
                    {
                        repositoriesFolder = projectFolder.Parent;
                    }
                    if (repositoriesFolder != null && repositoriesFolder.Exists)
                    {
                        foreach (DirectoryInfo repository in repositoriesFolder.GetDirectories())
                        {
                            string candidate = Path.Combine(repository.FullName, "Orange-Tendering-System");
                            if (Directory.Exists(candidate))
                            {
                                folders.Add(candidate);
                            }
                        }
                    }
                }

                // Each folder was used with and without a trailing slash. Configured extra scopes come first.
                List<string> scopes = new List<string>();
                foreach (string scope in options.LegacyKeyScopes)
                {
                    AddOnce(scopes, scope);
                }
                foreach (string folder in folders)
                {
                    string withoutSlash = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    AddOnce(scopes, withoutSlash);
                    AddOnce(scopes, withoutSlash + Path.DirectorySeparatorChar);
                }

                foreach (string scope in scopes)
                {
                    IDataProtectionProvider provider = DataProtectionProvider.Create(keyFolder, builder => builder.SetApplicationName(scope));
                    protectors.Add(provider.CreateProtector(TemplatePurpose));
                }
            }

            oldProtectorsByRoot[root] = protectors;
            return protectors;
        }
    }

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }
}
