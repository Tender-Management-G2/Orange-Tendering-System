using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;
using QRCoder;

namespace Orange_Tendering_System.Services;

public sealed class BankGuaranteeVerification(IDataProtectionProvider protection, TimeProvider time)
{
    public const string IssuerCode = "CEDAR-DEMO";
    public const string IssuerName = "Housing Bank (Demo)";
    public const string IssuerNameArabic = "بنك الإسكان";
    public const string RouteName = "BankGuaranteeVerification";

    private readonly ITimeLimitedDataProtector protector =
        protection.CreateProtector("Orange.BankGuaranteeVerification.v1").ToTimeLimitedDataProtector();

    public sealed record Details(string Number, decimal Amount, string Currency, DateOnly ExpiresOn);
    public enum Status { Matched, Expired, NoMatch, LinkUnavailable }
    public sealed record Result(Status State, Details? Issued = null);
    public sealed record Review(int DocumentId, string VerificationPath, string QrImage);

    // Issuer-side demo records are fixed independently of tender and supplier input.
    private static readonly Details ActiveExample = new("BG-DEMO-1001", 500m, "JOD", new DateOnly(2030, 12, 31));
    private static readonly IReadOnlyDictionary<string, Details> Registry = new Dictionary<string, Details>(StringComparer.Ordinal)
    {
        [ActiveExample.Number] = ActiveExample,
        ["BG-DEMO-1002"] = new("BG-DEMO-1002", 500m, "JOD", new DateOnly(2025, 12, 31)),
        ["BG-DEMO-1003"] = new("BG-DEMO-1003", 750m, "USD", new DateOnly(2030, 12, 31))
    };

    public static bool HasCompleteDetails(BidBankGuarantee? guarantee) =>
        guarantee is { Issuer: IssuerCode, Number.Length: > 0 and <= 40, Amount: > 0 and <= 999999999, ExpiresOn: not null }
        && CurrencyRules.Offer.Contains(guarantee.Currency!)
        && decimal.Round(guarantee.Amount.Value, CurrencyRules.Decimals(guarantee.Currency)) == guarantee.Amount;

    public string IssueLinkToken(BidBankGuarantee guarantee)
    {
        var details = new Details(guarantee.Number!, guarantee.Amount!.Value, guarantee.Currency!, guarantee.ExpiresOn!.Value);
        return protector.Protect(JsonSerializer.Serialize(details), TimeSpan.FromDays(1));
    }

    public static string QrImage(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(5));
    }

    public Result CheckLink(string token)
    {
        if (token.Length > 1024) return new(Status.LinkUnavailable);
        try
        {
            var submitted = JsonSerializer.Deserialize<Details>(protector.Unprotect(token));
            return submitted is null ? new(Status.LinkUnavailable) : Match(submitted);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return new(Status.LinkUnavailable);
        }
    }

    public Result Demo() => Match(ActiveExample);

    private Result Match(Details submitted)
    {
        if (!Registry.TryGetValue(submitted.Number, out var issued) || issued != submitted)
            return new(Status.NoMatch);
        var today = DateOnly.FromDateTime(time.GetUtcNow().ToJordan());
        return new(issued.ExpiresOn < today ? Status.Expired : Status.Matched, issued);
    }
}
