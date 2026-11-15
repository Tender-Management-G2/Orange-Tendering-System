using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// The single catalog of feedback for tender workflow and report actions (WorkflowControllerBase / ReportControllerBase).
// It is consulted only after the action committed. Messages say what happened; committee approvals come from the
// recorded votes, so one member's vote is never shown as full committee approval.
public static class OrangeWorkflowFeedback
{
    public static async Task<OrangeFeedback?> For(ControllerBase controller, TenderWorkflowService workflow, int tenderId)
    {
        var area = controller.RouteData.Values["area"]?.ToString() ?? "";
        var action = controller.RouteData.Values["action"]?.ToString() ?? "";
        var form = controller.Request.HasFormContentType ? controller.Request.Form : null;
        string Field(string name) => form?[name].ToString() ?? "";

        return (area, action) switch
        {
            // Sourcing Officer
            ("SourcingOfficer", "RequestWeights") => OrangeFeedback.Toast("Weight proposals requested", "The Evaluation Committee was asked to propose weights.", OrangeVariant.Send),
            ("SourcingOfficer", "ApproveWeights") => OrangeFeedback.Popup("Weights approved!", "The approved weights are frozen for this evaluation.", OrangeVariant.Approve),
            ("SourcingOfficer", "OpenBidding") => OrangeFeedback.Popup("Bidding opened!", "Invited suppliers can submit offers until the deadline.", OrangeVariant.Send),
            ("SourcingOfficer", "SetMissingDeadline") => OrangeFeedback.Toast("Deadline saved", "The bidding deadline is recorded."),
            ("SourcingOfficer", "Invite") => OrangeFeedback.Toast("Invitation sent", "The supplier was invited to this round.", OrangeVariant.Send),
            ("SourcingOfficer", "InviteExternal") => OrangeFeedback.Toast("External supplier invited", "The invitation e-mail is queued for delivery.", OrangeVariant.Send),
            ("SourcingOfficer", "CloseBidding") => OrangeFeedback.Popup("Bidding closed", "Submitted offers are locked and their compliance check was recorded."),
            ("SourcingOfficer", "Retender") => OrangeFeedback.Popup("New round started!", "The tender continues in a new bidding round."),
            ("SourcingOfficer", "Proceed") => OrangeFeedback.Popup("Moved to evaluation", "The tender continues with the submitted offers."),
            ("SourcingOfficer", "RequestProceed") => OrangeFeedback.Toast("Exception request sent", "The Sourcing Manager was asked to approve proceeding.", OrangeVariant.Send),
            ("SourcingOfficer", "Compliance") => OrangeFeedback.Toast("Compliance recorded", "The compliance decision for this offer is saved."),
            ("SourcingOfficer", "Setup") => OrangeFeedback.Popup("Evaluation scheduled!", "The committee session and scoring window are set.", OrangeVariant.Save),
            ("SourcingOfficer", "EndEvaluation") => OrangeFeedback.Popup("Evaluation ended", "Scoring is closed for this session."),
            ("SourcingOfficer", "Analyze") => OrangeFeedback.Toast("AI analysis ready", "Advisory only: it never selects the winner."),
            ("SourcingOfficer", "ReviewAi") => OrangeFeedback.Toast("AI analysis reviewed", "Your review of the advisory analysis is recorded."),
            ("SourcingOfficer", "RegenerateReport") => OrangeFeedback.Toast("Report regenerated", "A new report version was generated."),
            ("SourcingOfficer", "SendReport") => OrangeFeedback.Popup("Report sent!", "The Evaluation Committee can now review and sign it.", OrangeVariant.Send),
            ("SourcingOfficer", "ReviseReport") => OrangeFeedback.Popup("Correction submitted", "A corrected report version was created."),
            ("SourcingOfficer", "Forward" or "SubmitToManager") => OrangeFeedback.Popup("Report forwarded!", "The finalized report is with the Sourcing Manager for review.", OrangeVariant.Send),
            ("SourcingOfficer", "AssignTop") => OrangeFeedback.Popup("Top Committee assigned", "The four executives were notified to review the report.", OrangeVariant.Send),
            ("SourcingOfficer", "Award") => OrangeFeedback.Popup("Tender awarded!", "The award is recorded and the tender is closed. Suppliers are told when you notify them.", OrangeVariant.Award),
            ("SourcingOfficer" or "Supplier", "Upload") => OrangeFeedback.Toast("File uploaded", "The document is stored with this tender.", OrangeVariant.Upload),

            // Evaluation Committee
            ("EvaluationCommittee", "Propose") => OrangeFeedback.Toast("Weights proposed", "Your weight proposal is recorded."),
            ("EvaluationCommittee", "SubmitEvaluation") => OrangeFeedback.Popup("Evaluation submitted!", "Your scores are recorded for this evaluation round."),
            ("EvaluationCommittee", "GenerateReport") => OrangeFeedback.Popup("Report generated", "The evaluation report was created from the recorded scores.", OrangeVariant.Save),
            ("EvaluationCommittee", "FinalizeReport" or "Sign") => await Committee(workflow, tenderId, CommitteeType.Evaluation, "Evaluation Committee"),

            // Sourcing Manager
            ("SourcingManager", "DecideProceed") => Field("approve").Equals("true", StringComparison.OrdinalIgnoreCase)
                ? OrangeFeedback.Popup("Exception approved", "The Sourcing Officer may proceed with the current offers.", OrangeVariant.Approve)
                : OrangeFeedback.Popup("Exception declined", "The decision and its reason are recorded.", OrangeVariant.Return),
            ("SourcingManager", "Decision") => Field("decision") switch
            {
                "Approve" => OrangeFeedback.Popup("Report approved!", "Your approval and signature are recorded.", OrangeVariant.Approve),
                "Return" => OrangeFeedback.Popup("Report returned for correction", "The reason is recorded and sent back for correction.", OrangeVariant.Return),
                "Reject" => OrangeFeedback.Popup("Report rejected", "The rejection and its reason are recorded.", OrangeVariant.Return),
                _ => null
            },

            // Top Committee
            ("TopCommittee", "Vote") => Field("decision") == nameof(VoteDecision.Reject)
                ? OrangeFeedback.Popup("Rejection recorded", "Your decision and reason are recorded and stay visible.", OrangeVariant.Return)
                : await Committee(workflow, tenderId, CommitteeType.Top, "Top Committee"),

            // Supplier
            ("Supplier", "Respond") => Field("accept").Equals("true", StringComparison.OrdinalIgnoreCase)
                ? OrangeFeedback.Popup("Invitation accepted!", "You can now prepare and submit your offer.", OrangeVariant.Approve)
                : OrangeFeedback.Toast("Invitation declined", "Your response is recorded.", OrangeVariant.Return),
            ("Supplier", "SubmitBid") => OrangeFeedback.Popup("Offer submitted!", "Your offer is recorded for this tender round."),

            // Report lifecycle (ReportControllerBase)
            (_, "Return") => OrangeFeedback.Popup("Report returned for correction", "The reason is recorded and a correction is requested.", OrangeVariant.Return),
            (_, "ActivateRevision") => OrangeFeedback.Toast("Revision authorized", "The correction can now be prepared."),
            (_, "Resubmit") => OrangeFeedback.Popup("Corrected report submitted", "A new report version was generated. All required signatures must be collected again."),
            _ => null
        };
    }

    private static async Task<OrangeFeedback> Committee(TenderWorkflowService workflow, int tenderId, CommitteeType type, string name)
    {
        var approvals = await workflow.CurrentRoundApprovals(tenderId, type);
        return approvals is int count
            ? OrangeFeedback.CommitteeApproval(name, count)
            : OrangeFeedback.Popup("Approval recorded", "Your signed approval is recorded.", OrangeVariant.Approve);
    }
}
