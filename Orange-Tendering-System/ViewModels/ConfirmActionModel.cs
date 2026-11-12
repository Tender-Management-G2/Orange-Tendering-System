namespace Orange_Tendering_System.ViewModels
{
    // ORANGE TENDER MANAGEMENT SYSTEM
    // Owner: ALL TEAM — Integration (used by Haya, Doulat, Saja, Amal and the Supplier portal pages)
    // Purpose: Input for Views/Shared/_ConfirmAction.cshtml, the shared "are you sure?" panel for
    //          workflow decisions (approve, reject, return, forward, accept, decline, vote, ...).
    // Status: Display only. The rendered form is disabled; each owner adds a real POST action later.
    public record ConfirmActionModel(
        string ActionLabel,
        string Description,
        string Consequence,
        bool RequiresComment = false,
        bool IsDestructive = false);
}
