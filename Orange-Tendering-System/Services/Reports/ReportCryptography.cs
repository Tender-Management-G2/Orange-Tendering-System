using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Reports;

public interface IReportCertificateProvider { X509Certificate2? GetCertificate(string userId); }
public class StoreReportCertificateProvider(IOptions<ReportSigningOptions> options) : IReportCertificateProvider
{
    public X509Certificate2? GetCertificate(string userId)
    {
        if (!options.Value.UserCertificateThumbprints.TryGetValue(userId, out var thumbprint)) return null;
        TenderWorkflowService.Require(options.Value.UserCertificateThumbprints.Count(x => string.Equals(x.Value.Replace(" ", ""), thumbprint.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) == 1,
            "A personal signing credential cannot be mapped to multiple users.");
        TenderWorkflowService.Require(Enum.TryParse<StoreLocation>(options.Value.StoreLocation, out var location), "Invalid certificate store location.");
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        TenderWorkflowService.Require(matches.Count == 1, "Configured personal signing certificate is unavailable. No downgrade to electronic acknowledgment is allowed.");
        return matches[0];
    }
}
public class ReportCryptography(IReportCertificateProvider certificates, IOptions<ReportSigningOptions> options)
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static string Manifest(ReportSignature s, int version) => JsonSerializer.Serialize(new
    {
        Purpose = "Orange tender report endorsement", ReportId = s.TenderReportId, Version = version,
        s.UserId, s.SignerName, s.Role, Stage = s.Stage.ToString(), s.Slot, s.Decision,
        SignedUtc = DateTime.SpecifyKind(s.SignedAt, DateTimeKind.Utc).ToString("O"),
        s.DocumentHash, s.SnapshotHash
    });
    public void Sign(ReportSignature signature, int version)
    {
        signature.ManifestJson = Manifest(signature, version);
        using var certificate = certificates.GetCertificate(signature.UserId);
        if (certificate == null)
        {
            TenderWorkflowService.Require(!options.Value.RequireCertificateBacked, "Trusted personal signing credentials are required but are not configured.");
            signature.Method = SignatureMethod.ElectronicAcknowledgment;
            return;
        }
        TenderWorkflowService.Require(certificate.HasPrivateKey && Trusted(certificate) && InValidityWindow(certificate, signature.SignedAt), "A valid trusted personal certificate with a digital-signature private key is required.");
        var signed = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(signature.ManifestJson)), detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.ExcludeRoot };
        signed.ComputeSignature(signer, silent: true);
        signed.CheckSignature(verifySignatureOnly: false);
        signature.CmsSignature = signed.Encode(); signature.CertificateThumbprint = certificate.Thumbprint;
        signature.Method = SignatureMethod.CertificateCms;
    }
    public static bool Trusted(X509Certificate2 certificate)
    {
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (usage != null && !usage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature)) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
        return chain.Build(certificate);
    }
    public static bool Verify(ReportSignature signature, TenderReport report)
    {
        if (signature.DocumentHash != report.PdfSha256 || signature.SnapshotHash != report.SnapshotSha256 ||
            signature.ManifestJson != Manifest(signature, report.VersionNumber)) return false;
        if (signature.Method == SignatureMethod.ElectronicAcknowledgment) return signature.CmsSignature == null;
        try
        {
            var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(signature.ManifestJson)), detached: true);
            cms.Decode(signature.CmsSignature!); cms.CheckSignature(verifySignatureOnly: true);
            return cms.SignerInfos.Count == 1 && cms.SignerInfos[0].Certificate?.Thumbprint == signature.CertificateThumbprint && InValidityWindow(cms.SignerInfos[0].Certificate!, signature.SignedAt);
        }
        catch (CryptographicException) { return false; }
    }
    private static bool InValidityWindow(X509Certificate2 certificate, DateTime time)
    {
        var utc = DateTime.SpecifyKind(time, DateTimeKind.Utc);
        return certificate.NotBefore.ToUniversalTime() <= utc && utc <= certificate.NotAfter.ToUniversalTime();
    }
    public static bool CertificateTrustValid(ReportSignature signature)
    {
        if (signature.Method != SignatureMethod.CertificateCms) return false;
        try
        {
            var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(signature.ManifestJson)), true);
            cms.Decode(signature.CmsSignature!);
            return cms.SignerInfos.Count == 1 && cms.SignerInfos[0].Certificate is { } certificate && Trusted(certificate);
        }
        catch (CryptographicException) { return false; }
    }
}
