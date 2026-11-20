using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// One-step offer submission (no user-facing "Save draft"): the form, its attachments and the final submission are validated and
// committed together in one serializable transaction holding the tender row lock. A Draft row still exists for a moment inside
// that transaction (and for drafts saved before this change), because TenderWorkflowService.SubmitDraft is the single
// authoritative submission gate. All totals are calculated here, never taken from the browser.
public sealed partial class SupplierOfferService
{
    public const long MaxFileBytes = 10 * 1024 * 1024, MaxTotalBytes = 40 * 1024 * 1024;
    public const int MaxFiles = 15;
    public static readonly IReadOnlyList<string> PaymentTermOptions =
        ["100% on delivery and acceptance", "Net 30 days after delivery", "Net 60 days after delivery", "30% advance, 70% on delivery", "50% advance, 50% on delivery", "Milestone payments (describe in the technical proposal)"];

    // The page model: an accepted invitation without an offer, or with the supplier's own unsubmitted draft (resumed).
    public async Task<SupplierDraftOfferViewModel?> GetOfferFormAsync(string userId, int invitationId)
    {
        var invitation = await AvailableInvitations(userId, invitationId).AsNoTracking()
            .Include(i => i.Supplier).Include(i => i.TenderRound).ThenInclude(r => r.Tender).ThenInclude(t => t.Items).ThenInclude(x => x.Category)
            .Include(i => i.Bids).ThenInclude(b => b.Items).Include(i => i.Bids).ThenInclude(b => b.Documents)
            .AsSplitQuery().SingleOrDefaultAsync();
        if (invitation == null || invitation.Bids.Any(b => b.Status != BidStatus.Draft)) return null;
        var tender = invitation.TenderRound.Tender;
        var draft = invitation.Bids.OrderByDescending(b => b.Id).FirstOrDefault();
        var neededBy = await context.PurchaseRequests.Where(p => p.Id == tender.PurchaseRequestId).Select(p => p.NeededBy).SingleAsync();
        var model = new SupplierDraftOfferViewModel
        {
            InvitationId = invitation.Id, BidId = draft?.Id, TenderId = tender.Id, TenderTitle = tender.Title, CompanyName = invitation.Supplier.CompanyName,
            Deadline = invitation.TenderRound.Deadline, NeededBy = neededBy, RequiredDocuments = OfferDocumentTypes.Parse(tender.RequiredDocuments),
            Requirements = PublicRequirements(tender),
            ItemContext = tender.Items.OrderBy(x => x.Id).Select(x => new OfferItemContext(x.Id, x.Name, x.Category?.Name, x.Specifications, x.Quantity, x.Unit)).ToList(),
            ExistingDocuments = draft?.Documents.Select(d => new SupplierDocumentViewModel { Id = d.Id, FileName = d.FileName, DocumentType = d.DocumentType, SizeBytes = d.SizeBytes, UploadedAt = d.UploadedAt }).ToList() ?? []
        };
        model.Input = draft == null ? new SupplierDraftOfferInputModel { ValidUntil = (invitation.TenderRound.Deadline ?? workflow.Now).Date.AddDays(90) } : ToInput(draft);
        model.Input.Items = tender.Items.OrderBy(x => x.Id).Select(x =>
        {
            var line = draft?.Items.FirstOrDefault(i => i.TenderItemId == x.Id);
            return new SupplierDraftItemInputModel { TenderItemId = x.Id, Name = x.Name, RequiredQuantity = x.Quantity, Quantity = x.Quantity,
                UnitPrice = line?.UnitPrice ?? 0, OfferedProduct = line?.OfferedProduct, OfferedSpecifications = line?.OfferedSpecifications, Notes = line?.Notes };
        }).ToList();
        return model;
    }

    // Tender requirements a supplier may read (the published requirement texts; never weights, committee or internal data).
    private static List<(string, string)> PublicRequirements(Tender tender)
    {
        var rows = new List<(string, string)>();
        var labels = new Dictionary<string, string> { ["TechnicalRequirements"] = "Technical requirements", ["CommercialConditions"] = "Commercial conditions",
            ["DeliveryExpectations"] = "Delivery", ["MandatoryCompliance"] = "Mandatory requirements" };
        try
        {
            using var json = JsonDocument.Parse(tender.Requirements ?? "{}");
            foreach (var p in json.RootElement.EnumerateObject())
                if (labels.TryGetValue(p.Name, out var label) && p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString())) rows.Add((label, p.Value.GetString()!));
        }
        catch (JsonException) { if (!string.IsNullOrWhiteSpace(tender.Requirements)) rows.Add(("Requirements", tender.Requirements)); }
        return rows;
    }

    private static SupplierDraftOfferInputModel ToInput(Bid bid) => new()
    {
        CurrencyCode = bid.CurrencyCode, DeliveryDays = bid.DeliveryDays, PaymentTerms = bid.PaymentTerms, TechnicalProposal = bid.TechnicalProposal, ValidUntil = bid.ValidUntil,
        IncludeShipping = bid.ShippingCost > 0, ShippingCost = bid.ShippingCost, IncludeInstallation = bid.InstallationCost > 0, InstallationCost = bid.InstallationCost,
        IncludeMaintenance = bid.MaintenanceCost > 0, MaintenanceCost = bid.MaintenanceCost, MaintenanceDescription = bid.MaintenanceDescription,
        IncludeOther = bid.OtherCost > 0, OtherCost = bid.OtherCost, OtherCostDescription = bid.OtherCostDescription,
        TaxMode = bid.TaxRate.HasValue || bid.TaxAmount == 0 ? "Rate" : "Amount", TaxRate = bid.TaxRate, TaxIncludesCharges = bid.TaxIncludesCharges, TaxAmount = bid.TaxAmount,
        WarrantyMonths = bid.WarrantyMonths, WarrantyType = bid.WarrantyType, WarrantyTerms = bid.WarrantyTerms, Deviations = bid.Deviations, AfterSalesSupport = bid.AfterSalesSupport
    };

    public sealed record UploadedOfferFile(string DocumentType, string FileName, string ContentType, byte[] Bytes);

    // Validates every field and file, then creates/updates the bid, stores the files and submits it, all or nothing.
    // Returns the submitted bid id, or the list of problems (nothing is saved then).
    public async Task<(int? BidId, List<string> Errors)> SubmitOfferAsync(string userId, int invitationId, SupplierDraftOfferInputModel input, IReadOnlyList<UploadedOfferFile> files)
    {
        var errors = new List<string>();
        var form = await GetOfferFormAsync(userId, invitationId);
        if (form == null)
        {
            var existing = await OwnOffers(userId).Where(b => b.TenderInvitationId == invitationId && b.Status == BidStatus.Submitted).Select(b => (int?)b.Id).FirstOrDefaultAsync();
            return (null, [existing != null ? "This offer was already submitted. Submitted offers cannot be changed." : await GetCreateBlockReasonAsync(userId, invitationId)]);
        }
        var bid = Validate(form, input, files, errors);
        if (errors.Count > 0) return (null, errors);

        var written = new List<string>();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await context.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={form.TenderId}").SingleAsync();
            // Re-read under the lock: a double click or a second tab finds the offer already submitted here.
            var current = await context.Bids.Include(b => b.Items).Include(b => b.Documents).Where(b => b.TenderInvitationId == invitationId).ToListAsync();
            if (current.Any(b => b.Status != BidStatus.Draft)) return (null, ["This offer was already submitted. Submitted offers cannot be changed."]);
            var target = current.FirstOrDefault();
            if (target == null) { target = new Bid { TenderInvitationId = invitationId, RevisionNumber = 1, Status = BidStatus.Draft, CreatedAt = workflow.Now }; context.Bids.Add(target); }
            CopyTerms(bid, target);
            context.BidItems.RemoveRange(target.Items.Where(i => bid.Items.All(n => n.TenderItemId != i.TenderItemId)));
            foreach (var line in bid.Items)
            {
                var item = target.Items.FirstOrDefault(i => i.TenderItemId == line.TenderItemId);
                if (item == null) target.Items.Add(item = new BidItem { TenderItemId = line.TenderItemId });
                item.Quantity = line.Quantity; item.UnitPrice = line.UnitPrice; item.OfferedProduct = line.OfferedProduct; item.OfferedSpecifications = line.OfferedSpecifications; item.Notes = line.Notes;
            }
            // Attachments removed before submission are detached from the offer (rows and files of a draft are not history yet).
            var removedFiles = new List<string>();
            foreach (var doc in target.Documents.Where(d => input.RemoveDocumentIds.Contains(d.Id)).ToList())
            {
                try { removedFiles.Add(PrivateDocumentPaths.Resolve(environment.ContentRootPath, doc.StoragePath)); } catch (WorkflowException) { }
                context.Documents.Remove(doc);
            }
            await context.SaveChangesAsync();
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var relative = Path.Combine("App_Data", "SupplierUploads", Guid.NewGuid().ToString("N") + extension);
                var full = Path.Combine(environment.ContentRootPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllBytesAsync(full, file.Bytes); written.Add(full);
                context.Documents.Add(new Document { BidId = target.Id, FileName = file.FileName, StoragePath = relative, ContentType = file.ContentType, SizeBytes = file.Bytes.Length,
                    DocumentType = file.DocumentType, UploadedAt = workflow.Now, UploadedByUserId = userId });
            }
            context.AuditLogs.Add(new AuditLog { UserId = userId, EntityName = nameof(Bid), EntityId = target.Id.ToString(), Action = "OfferPrepared", CreatedAt = workflow.Now,
                NewValues = JsonSerializer.Serialize(new { Totals = OfferTotals.Of(bid), Files = files.Select(f => new { f.DocumentType, f.FileName, Size = f.Bytes.Length }) }) });
            await context.SaveChangesAsync();
            // The authoritative gate (owner, invitation, round open, deadline, weights, items) runs inside this same transaction.
            await workflow.SubmitDraft(target.Id, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "SupplierPortal")));
            await transaction.CommitAsync();
            foreach (var path in removedFiles) { try { File.Delete(path); } catch (IOException) { } }
            return (target.Id, []);
        }
        catch (WorkflowException e)
        {
            foreach (var path in written) File.Delete(path);
            context.ChangeTracker.Clear();
            return (null, [e.Message]);
        }
        catch
        {
            foreach (var path in written) File.Delete(path);
            throw;
        }
    }

    // Field rules. Money uses the currency's decimals (JOD 3, USD/EUR 2): a value with more decimals is refused, not rounded.
    private Bid Validate(SupplierDraftOfferViewModel form, SupplierDraftOfferInputModel input, IReadOnlyList<UploadedOfferFile> files, List<string> errors)
    {
        var currency = input.CurrencyCode;
        if (!CurrencyRules.Offer.Contains(currency)) { errors.Add("Choose JOD, USD or EUR."); currency = "JOD"; }
        var dec = CurrencyRules.Decimals(currency);
        void Money(decimal value, string label) { if (value < 0 || value > 999_999_999_999m || decimal.Round(value, dec) != value) errors.Add($"{label}: enter an amount of zero or more with at most {dec} decimal places in {currency}."); }
        string? Text(string? value, int max, string label, bool required)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) { if (required) errors.Add(label + " is required."); return null; }
            if (v.Length > max) errors.Add($"{label} is limited to {max} characters.");
            return v;
        }
        var bid = new Bid { CurrencyCode = currency };
        input.Items ??= [];
        var expected = form.ItemContext.Select(x => x.TenderItemId).Order().ToList();
        if (!expected.SequenceEqual(input.Items.Select(x => x.TenderItemId).Order())) errors.Add("Quote every tender item exactly once. Items cannot be added or replaced.");
        foreach (var line in input.Items)
        {
            var item = form.ItemContext.FirstOrDefault(x => x.TenderItemId == line.TenderItemId);
            if (item == null) continue;
            // No partial offers: no approved business rule allows a different quantity.
            if (line.Quantity != item.Quantity) errors.Add($"{item.Name}: offer the requested quantity ({item.Quantity:0.###} {item.Unit}). Partial quantities are not accepted for this tender.");
            if (line.UnitPrice <= 0) errors.Add($"{item.Name}: enter a unit price above zero.");
            Money(line.UnitPrice, item.Name + " unit price");
            bid.Items.Add(new BidItem { TenderItemId = item.TenderItemId, Quantity = item.Quantity, UnitPrice = line.UnitPrice,
                OfferedProduct = Text(line.OfferedProduct, 300, item.Name + " — offered product", true),
                OfferedSpecifications = Text(line.OfferedSpecifications, 4000, item.Name + " — offered specifications", true),
                Notes = Text(line.Notes, 2000, item.Name + " — notes", false) });
        }
        if (input.DeliveryDays is not (>= 1 and <= 3650)) errors.Add("Delivery lead time: enter whole calendar days from 1 to 3650.");
        bid.DeliveryDays = input.DeliveryDays;
        bid.PaymentTerms = Text(input.PaymentTerms, 1000, "Payment terms", true);
        var deadline = form.Deadline?.Date ?? workflow.Now.Date;
        if (input.ValidUntil == null || input.ValidUntil.Value.Date < deadline) errors.Add($"Offer validity must last at least until the submission deadline ({deadline:d MMM yyyy}).");
        else if (input.ValidUntil.Value.Date > workflow.Now.Date.AddYears(3)) errors.Add("Offer validity cannot be more than three years ahead.");
        bid.ValidUntil = input.ValidUntil?.Date;
        bid.TechnicalProposal = Text(input.TechnicalProposal, 10000, "Technical proposal", true);
        if (input.WarrantyMonths is < 0 or > 240) errors.Add("Warranty period: 0 to 240 whole months.");
        bid.WarrantyMonths = input.WarrantyMonths; bid.WarrantyType = Text(input.WarrantyType, 60, "Warranty type", false);
        bid.WarrantyTerms = Text(input.WarrantyTerms, 2000, "Warranty conditions", false);
        bid.Deviations = Text(input.Deviations, 4000, "Deviations", false);
        bid.AfterSalesSupport = Text(input.AfterSalesSupport, 2000, "After-sales support", false);

        bid.ShippingCost = input.IncludeShipping ? input.ShippingCost : 0; Money(bid.ShippingCost, "Shipping cost");
        bid.InstallationCost = input.IncludeInstallation ? input.InstallationCost : 0; Money(bid.InstallationCost, "Installation cost");
        bid.MaintenanceCost = input.IncludeMaintenance ? input.MaintenanceCost : 0; Money(bid.MaintenanceCost, "Maintenance cost");
        bid.MaintenanceDescription = input.IncludeMaintenance ? Text(input.MaintenanceDescription, 1000, "Maintenance coverage and duration", true) : null;
        bid.OtherCost = input.IncludeOther ? input.OtherCost : 0; Money(bid.OtherCost, "Other costs");
        bid.OtherCostDescription = input.IncludeOther ? Text(input.OtherCostDescription, 500, "Description of other costs", true) : null;
        foreach (var (on, value, label) in new[] { (input.IncludeShipping, input.ShippingCost, "Shipping"), (input.IncludeInstallation, input.InstallationCost, "Installation"), (input.IncludeMaintenance, input.MaintenanceCost, "Maintenance"), (input.IncludeOther, input.OtherCost, "Other costs") })
            if (on && value <= 0) errors.Add($"{label}: enter the amount, or untick it when it is included in the unit prices or does not apply.");

        var subtotal = bid.Items.Sum(i => i.Quantity * i.UnitPrice);
        var charges = bid.ShippingCost + bid.InstallationCost + bid.MaintenanceCost + bid.OtherCost;
        if (input.TaxMode == "Rate")
        {
            if (input.TaxRate is not (>= 0 and <= 100) || decimal.Round(input.TaxRate.Value, 2) != input.TaxRate) errors.Add("Tax rate: enter a percentage from 0 to 100 with at most two decimals.");
            bid.TaxRate = input.TaxRate; bid.TaxIncludesCharges = input.TaxIncludesCharges;
            // Tax is calculated once, on the stated taxable base, and rounded to the currency's precision (never counted twice).
            bid.TaxAmount = decimal.Round((subtotal + (input.TaxIncludesCharges ? charges : 0)) * (input.TaxRate ?? 0) / 100m, dec, MidpointRounding.AwayFromZero);
        }
        else if (input.TaxMode == "Amount") { bid.TaxRate = null; bid.TaxIncludesCharges = false; bid.TaxAmount = input.TaxAmount; Money(bid.TaxAmount, "Tax amount"); }
        else errors.Add("Choose how tax is stated: a tax rate or a total tax amount.");

        // Attachments: every document type the tender requires must be present (new upload or kept draft attachment).
        var kept = form.ExistingDocuments.Where(d => !input.RemoveDocumentIds.Contains(d.Id)).ToList();
        if (files.Count + kept.Count == 0) errors.Add("Attach at least one supporting document (PDF, PNG or JPEG).");
        foreach (var key in form.RequiredDocuments)
            if (files.All(f => f.DocumentType != key) && kept.All(d => d.DocumentType != key)) errors.Add($"Required document missing: {OfferDocumentTypes.Label(key)}.");
        if (files.Count > MaxFiles || files.Sum(f => (long)f.Bytes.Length) > MaxTotalBytes) errors.Add($"Attach at most {MaxFiles} files, {MaxTotalBytes / 1024 / 1024} MB in total.");
        return bid;
    }

    private static void CopyTerms(Bid from, Bid to)
    {
        to.CurrencyCode = from.CurrencyCode; to.DeliveryDays = from.DeliveryDays; to.PaymentTerms = from.PaymentTerms; to.TechnicalProposal = from.TechnicalProposal; to.ValidUntil = from.ValidUntil;
        to.ShippingCost = from.ShippingCost; to.InstallationCost = from.InstallationCost; to.MaintenanceCost = from.MaintenanceCost; to.MaintenanceDescription = from.MaintenanceDescription;
        to.OtherCost = from.OtherCost; to.OtherCostDescription = from.OtherCostDescription; to.TaxAmount = from.TaxAmount; to.TaxRate = from.TaxRate; to.TaxIncludesCharges = from.TaxIncludesCharges;
        to.WarrantyMonths = from.WarrantyMonths; to.WarrantyType = from.WarrantyType; to.WarrantyTerms = from.WarrantyTerms; to.Deviations = from.Deviations; to.AfterSalesSupport = from.AfterSalesSupport;
    }

    // File checks by content signature, not by the browser's content type.
    public static (UploadedOfferFile? File, string? Error) ReadUpload(string documentType, string fileName, byte[] bytes)
    {
        var name = Path.GetFileName(fileName);
        if (OfferDocumentTypes.Find(documentType) == null) return (null, "Unknown document type.");
        if (name.Length is 0 or > 260) return (null, "Invalid file name.");
        if (bytes.Length is 0 or > (int)MaxFileBytes) return (null, $"{name}: files must be between 1 byte and 10 MB.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var type = extension switch
        {
            ".pdf" when bytes.AsSpan().StartsWith("%PDF-"u8) => "application/pdf",
            ".png" when bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
            ".jpg" or ".jpeg" when bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 => "image/jpeg",
            _ => null
        };
        return type == null ? (null, $"{name}: only valid PDF, PNG or JPEG files are accepted.") : (new UploadedOfferFile(documentType, name, type, bytes), null);
    }
}
