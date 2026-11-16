using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

static class RetryTop {
 public static async Task Run(WebApplicationFactory<WebAppMarker> app) {
  const int id=4; string officer="",manager="";int reportId=0;string hash="";
  var members=new List<string>();
  ClaimsPrincipal P(string u)=>new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,u)],"demo-fixture"));
  async Task Db(Func<ApplicationDbContext,Task> f){using var sc=app.Services.CreateScope();await f(sc.ServiceProvider.GetRequiredService<ApplicationDbContext>());}
  async Task Flow(Func<TenderWorkflowService,Task> f){using var sc=app.Services.CreateScope();await f(sc.ServiceProvider.GetRequiredService<TenderWorkflowService>());}
  await Db(async db=>{
   var t=await db.Tenders.Include(x=>x.Items).SingleAsync(x=>x.Id==id);
   if(!t.Title.StartsWith("TARIQ DEMO - ")||t.AwardedBidId!=null)throw new Exception("Unawarded synthetic demo #4 only.");
   officer=await db.Users.Where(x=>x.Email=="sourcing.officer@orange-demo.local"&&x.IsActive).Select(x=>x.Id).SingleAsync();
   var seats=await db.CommitteeMembers.Where(x=>x.TenderId==id&&x.CommitteeType==CommitteeType.Evaluation).ToArrayAsync();
   manager=seats.Single(x=>x.Responsibility==CommitteeResponsibility.SourcingManager).UserId;members=seats.Select(x=>x.UserId).ToList();
   var round=await db.TenderRounds.Where(x=>x.TenderId==id).OrderByDescending(x=>x.RoundNumber).FirstAsync();
   if(round.RoundNumber!=2)throw new Exception("Expected the already-published second round of demo #4.");
   if(await db.Set<EvaluationSession>().AnyAsync(x=>x.TenderRoundId==round.Id))return;
   if(t.Status!=TenderStatus.Bidding)throw new Exception("Second round must be awaiting bids.");
   var invites=await db.TenderInvitations.Include(x=>x.Supplier).Where(x=>x.TenderRoundId==round.Id).OrderBy(x=>x.Supplier.Email).ToArrayAsync();
   var expected=Enumerable.Range(30,3).Select(n=>$"SupplierTariq{n}@Demo-orange.loacl").ToArray();
   invites=invites.Where(x=>expected.Contains(x.Supplier.Email)).ToArray();
   if(!invites.Select(x=>x.Supplier.Email).SequenceEqual(expected)||await db.Bids.AnyAsync(x=>x.TenderInvitation.TenderRoundId==round.Id))throw new Exception("An untouched demo round with invitations to Tariq30-32 is required. Other invitations are preserved unchanged.");
   var previous=await db.Set<EvaluationSession>().AsNoTracking().Where(x=>x.TenderRound.TenderId==id).OrderByDescending(x=>x.Id).FirstAsync();
   var weights=await db.Set<WeightRound>().SingleAsync(x=>x.Id==previous.WeightRoundId);
   var now=DateTime.UtcNow;
   await using var tx=await db.Database.BeginTransactionAsync();
   round.OpensAt=now.AddDays(-3);round.Deadline=now.AddDays(-1);round.ClosedAt=now.AddDays(-1);round.Status=TenderRoundStatus.SentToEvaluation;t.Status=TenderStatus.Evaluation;
   var cr=new CommitteeRound{TenderRoundId=round.Id,CommitteeType=CommitteeType.Evaluation,RoundNumber=1,StartedAt=now.AddHours(-3),FinancialWeight=weights.ApprovedFinancial,TechnicalWeight=weights.ApprovedTechnical};db.Add(cr);await db.SaveChangesAsync();
   var criteria=new[]{new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Financial value",Type=CriterionType.Financial,Weight=weights.ApprovedFinancial,MaxScore=100,DisplayOrder=1},new EvaluationCriterion{CommitteeRoundId=cr.Id,Name="Technical compliance and warranty",Type=CriterionType.Technical,Weight=weights.ApprovedTechnical,MaxScore=100,DisplayOrder=2}};db.AddRange(criteria);
   var ai=new AiAnalysis{TenderRoundId=round.Id,Status="Completed",Model="synthetic-demo-analysis-not-live-AI",Findings="Owner-requested demo retry: three compliant synthetic offers; Tariq30 has the lowest price and highest weighted score. Not a live AI result.",EvidenceJson="{\"syntheticDemo\":true,\"retry\":true}",StartedAt=now.AddHours(-3),ReviewedAt=now.AddHours(-2),ReviewedByUserId=officer};db.Add(ai);await db.SaveChangesAsync();
   var session=new EvaluationSession{TenderRoundId=round.Id,WeightRoundId=weights.Id,CommitteeRoundId=cr.Id,AiAnalysisId=ai.Id,State=SessionState.Scheduled,StartsAt=now.AddHours(-2),EndsAt=now.AddHours(1),MeetingUrl="https://example.invalid/demo-retry-no-live-meeting",FinalApprovalPolicyJson=previous.FinalApprovalPolicyJson};db.Add(session);await db.SaveChangesAsync();
   foreach(var seat in seats)db.Add(new EvaluationParticipation{EvaluationSessionId=session.Id,CommitteeMemberId=seat.Id,SentAt=now.AddHours(-3),SubmittedAt=now.AddHours(-1)});
   for(int i=0;i<invites.Length;i++){
    var inv=invites[i];inv.Status=InvitationStatus.Accepted;inv.RespondedAt=now.AddDays(-2);
    var bid=new Bid{TenderInvitationId=inv.Id,Status=BidStatus.Submitted,RevisionNumber=1,CreatedAt=now.AddDays(-2),SubmittedAt=now.AddDays(-2),CurrencyCode="JOD",DeliveryDays=10+i*3,PaymentTerms="30 days after delivery",ValidUntil=now.AddDays(60),ShippingCost=100+i*25,TechnicalProposal="Synthetic demo retry offer: Core i7, 16 GB RAM, 512 GB SSD, Windows 11 Pro, 3-year warranty.",ComplianceStatus=ComplianceStatus.Compliant,ComplianceNotes="Synthetic compliance assessment for owner-requested retry.",ComplianceCheckedByUserId=officer,ComplianceCheckedAt=now.AddHours(-3),Items=t.Items.Select(x=>new BidItem{TenderItemId=x.Id,Quantity=x.Quantity,UnitPrice=650+i*50,OfferedSpecifications=x.Specifications}).ToList()};db.Add(bid);await db.SaveChangesAsync();db.Add(new SessionBid{EvaluationSessionId=session.Id,BidId=bid.Id});
    foreach(var seat in seats)foreach(var criterion in criteria)db.Add(new EvaluationScore{BidId=bid.Id,CommitteeMemberId=seat.Id,EvaluationCriterionId=criterion.Id,Score=criterion.Type==CriterionType.Financial?100m-i*7m:95m-i*4m,Comment="Synthetic retry score, not a real procurement decision.",ScoredAt=now.AddHours(-1)});
   }
   db.Add(new AuditLog{UserId=officer,Action="TariqDemoRoundRetryPrepared",EntityName="Tender",EntityId="4",CreatedAt=now,NewValues=JsonSerializer.Serialize(new{round.Id,round.RoundNumber,Reason="Owner asked to replay at Top; old votes, signatures, deliveries and decisions preserved. All new evaluation actions are synthetic demo actions."})});
   await db.SaveChangesAsync();await tx.CommitAsync();
  });
  SessionState state=default;TenderStatus status=default;
  async Task Refresh(){await Db(async db=>{var s=await db.Set<EvaluationSession>().Include(x=>x.Report).Include(x=>x.TenderRound).ThenInclude(x=>x.Tender).Where(x=>x.TenderRound.TenderId==id).OrderByDescending(x=>x.TenderRound.RoundNumber).FirstAsync();state=s.State;status=s.TenderRound.Tender.Status;reportId=s.ReportId??0;hash=s.Report?.PdfSha256??"";});}
  await Refresh();
  if(state==SessionState.Scheduled){await Flow(w=>w.EndEvaluation(id,true,"Synthetic owner-requested retry of Top voting; prior round retained unchanged.",P(officer)));await Refresh();}
  if(state==SessionState.ReportDraft){
   bool sent=false;await Db(async db=>sent=await db.Set<EvaluationSession>().Where(x=>x.ReportId==reportId).Select(x=>x.ReportSentAt!=null).SingleAsync());
   if(!sent)await Flow(w=>w.SendReportToCommittee(id,P(officer)));
   foreach(var m in members){await Flow(async w=>{await w.ReportPdf(reportId,P(m));});await Flow(w=>w.FinalizeReport(id,"Synthetic demo retry approval only.",P(m),hash,true));}
   await Refresh();
  }
  if(status==TenderStatus.OfficerReview){await Flow(w=>w.Forward(id,P(officer)));await Refresh();}
  if(status==TenderStatus.ManagerReview){await Flow(async w=>{await w.ReportPdf(reportId,P(manager));});await Flow(w=>w.ManagerDecision(id,"Approve","Owner-requested synthetic retry: ready for Top voting.",P(manager),hash,true));await Refresh();}
  await Flow(async w=>{await w.VerifyReport(reportId,P(manager));});
  await Db(async db=>{
   var s=await db.Set<EvaluationSession>().SingleAsync(x=>x.ReportId==reportId);
   var r=await db.CommitteeRounds.SingleAsync(x=>x.TenderReportId==reportId&&x.CommitteeType==CommitteeType.Top);
   if(status!=TenderStatus.TopCommitteeReview||state!=SessionState.ManagerApproved||r.Status!=CommitteeRoundStatus.InProgress||await db.CommitteeVotes.AnyAsync(x=>x.CommitteeRoundId==r.Id)||await db.Set<ReportSignature>().AnyAsync(x=>x.TenderReportId==reportId&&x.Stage==ReportSigningStage.Top)||await db.Set<FinalDecisionNotice>().AnyAsync(x=>x.TenderReportId==reportId))throw new Exception("Fresh Top voting verification failed.");
   foreach(var u in await db.CommitteeMembers.Where(x=>x.TenderId==id&&x.CommitteeType==CommitteeType.Top).Select(x=>x.UserId).ToArrayAsync()){
    using var scope=app.Services.CreateScope();var w=scope.ServiceProvider.GetRequiredService<TenderWorkflowService>();var review=await w.ReviewReport(reportId,P(u));if(!review.CanVoteTop)throw new Exception("Top member cannot vote.");
   }
   Console.WriteLine($"PASS: Tender #4 round 2; report #{reportId}; TopCommitteeReview; all four Top members can vote; zero new Top votes/signatures; prior round history retained.");
   Console.WriteLine($"https://localhost:7182/TopCommittee/SignedReports/Review/{reportId}");
  });
 }
}
