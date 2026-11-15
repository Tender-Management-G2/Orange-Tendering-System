using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

public static class ClarificationWorkflowChecks
{
 public static async Task Run()
 {
  var connection=Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")!;
  var catalog=new SqlConnectionStringBuilder(connection).InitialCatalog;
  if(!catalog.StartsWith("OrangeAuthCheck_Clarifications_"))throw new Exception("Isolated SQL database required");
  var files=Path.Combine(Path.GetTempPath(),"OrangeClarifications_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(files);
  Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);Environment.SetEnvironmentVariable("DemoData__Enabled","false");
  using var app=new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b=>{b.UseContentRoot(files).UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
   ["ConnectionStrings:Default Connection"]=connection,["DemoData:Enabled"]="false",["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]="",["Biometric:SupplierPasswordOnly"]="true",["Logging:LogLevel:Default"]="Warning"}));});
  int pass=0;void Check(bool yes,string name){if(!yes)throw new Exception("FAIL "+name);pass++;Console.WriteLine("PASS "+name);}
  var ids=new Dictionary<string,string>();var companies=new Dictionary<string,int>();
  var pw="C!9a"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));int tender=0,invitation=0,bid=0,request=0,foreignRequest=0,evalSeat=0;
  var roles=new[]{SystemRoles.Admin,SystemRoles.RequestingDepartment,SystemRoles.SourcingOfficer,SystemRoles.Supplier,SystemRoles.EvaluationCommittee,SystemRoles.SourcingManager,SystemRoles.TopCommittee};
  async Task<T> Service<T>(Func<IServiceProvider,Task<T>> work){using var sc=app.Services.CreateScope();return await work(sc.ServiceProvider);}
  async Task Db(Func<ApplicationDbContext,Task> work){using var sc=app.Services.CreateScope();await work(sc.ServiceProvider.GetRequiredService<ApplicationDbContext>());}
  ClaimsPrincipal Principal(string role)=>new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,ids[role])],"fixture"));
  await Db(async db=>{
   Check(!await db.Users.AnyAsync(),"new isolated SQL database");
   var d=new Department{Name="Clarification fixture",IsActive=true};var other=new Department{Name="Other department",IsActive=true};db.AddRange(d,other);await db.SaveChangesAsync();
   using var sc=app.Services.CreateScope();var users=sc.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
   foreach(var role in roles.Append("SupplierTwo")){
    int? supplierId=null;if(role is SystemRoles.Supplier or "SupplierTwo"){var s=new Supplier{CompanyName=role+" test company",Email=Guid.NewGuid().ToString("N")+"@clarification.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};db.Add(s);await db.SaveChangesAsync();supplierId=s.Id;companies[role]=s.Id;}
    var email="u"+ids.Count+"@clarification.invalid";var u=new ApplicationUser{UserName=email,Email=email,FullName=role,IsActive=true,EmailConfirmed=true,SupplierId=supplierId,DepartmentId=role==SystemRoles.RequestingDepartment?d.Id:null};
    if(!(await users.CreateAsync(u,pw)).Succeeded||!(await users.AddToRoleAsync(u,role=="SupplierTwo"?SystemRoles.Supplier:role)).Succeeded)throw new Exception("Identity fixture failed");ids[role]=u.Id;
   }
   var pr=new PurchaseRequest{Title="Clarification request",DepartmentId=d.Id,CreatedByUserId=ids[SystemRoles.RequestingDepartment],Status=PurchaseRequestStatus.Accepted,Justification="SQL fixture"};
   var foreign=new PurchaseRequest{Title="Other department request",DepartmentId=other.Id,CreatedByUserId=ids[SystemRoles.SourcingOfficer],Status=PurchaseRequestStatus.Accepted,Justification="SQL fixture"};db.AddRange(pr,foreign);await db.SaveChangesAsync();request=pr.Id;foreignRequest=foreign.Id;
   var t=new Tender{Title="Clarification tender",PurchaseRequestId=pr.Id,CreatedByUserId=ids[SystemRoles.SourcingOfficer],Status=TenderStatus.Bidding};db.Add(t);await db.SaveChangesAsync();tender=t.Id;
   var round=new TenderRound{TenderId=t.Id,RoundNumber=1,Status=TenderRoundStatus.Open,OpensAt=DateTime.UtcNow.AddDays(-1),Deadline=DateTime.UtcNow.AddDays(1),CreatedByUserId=ids[SystemRoles.SourcingOfficer]};db.Add(round);await db.SaveChangesAsync();
   var invite=new TenderInvitation{TenderRoundId=round.Id,SupplierId=companies[SystemRoles.Supplier],InvitedByUserId=ids[SystemRoles.SourcingOfficer],InvitedAt=DateTime.UtcNow};db.Add(invite);await db.SaveChangesAsync();invitation=invite.Id;
   var evaluator=new CommitteeMember{TenderId=t.Id,UserId=ids[SystemRoles.EvaluationCommittee],CommitteeType=CommitteeType.Evaluation,Responsibility=CommitteeResponsibility.TechnicalRepresentative};db.Add(evaluator);
   db.AddRange(new CommitteeMember{TenderId=t.Id,UserId=ids[SystemRoles.SourcingManager],CommitteeType=CommitteeType.Evaluation,Responsibility=CommitteeResponsibility.SourcingManager},new CommitteeMember{TenderId=t.Id,UserId=ids[SystemRoles.TopCommittee],CommitteeType=CommitteeType.Top});await db.SaveChangesAsync();evalSeat=evaluator.Id;
  });
  int prebid=(await Service(sp=>sp.GetRequiredService<SupplierCommunicationService>().CreateAsync(ids[SystemRoles.Supplier],new SupplierClarificationInputModel{InvitationId=invitation,Subject="Pre-bid question",Message="Please clarify the requirements"})))!.Value;
  Check(prebid>0,"supplier starts invitation-linked clarification before any bid exists");
  Check(await Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids[SystemRoles.SourcingOfficer],prebid,"Officer pre-bid answer")),"Officer replies to pre-bid conversation");
  await Db(async db=>{var b=new Bid{TenderInvitationId=invitation,Status=BidStatus.Submitted,SubmittedAt=DateTime.UtcNow};db.Add(b);await db.SaveChangesAsync();bid=b.Id;});
  var pairs=new[]{(SystemRoles.RequestingDepartment,SystemRoles.SourcingOfficer),(SystemRoles.SourcingOfficer,SystemRoles.RequestingDepartment),(SystemRoles.SourcingOfficer,SystemRoles.Supplier),(SystemRoles.SourcingOfficer,SystemRoles.EvaluationCommittee),(SystemRoles.EvaluationCommittee,SystemRoles.SourcingOfficer),(SystemRoles.EvaluationCommittee,SystemRoles.SourcingManager),(SystemRoles.SourcingManager,SystemRoles.EvaluationCommittee),(SystemRoles.SourcingManager,SystemRoles.TopCommittee),(SystemRoles.TopCommittee,SystemRoles.SourcingManager)};
  var threads=new Dictionary<(string,string),int>();
  foreach(var (from,to) in pairs){
   var options=await Service(sp=>sp.GetRequiredService<ClarificationService>().Options(Principal(from)));var option=options.First(o=>o.RecipientId==ids[to]);
   int id=await Service(sp=>sp.GetRequiredService<ClarificationService>().CreateAsync(Principal(from),option.RecipientId,option.ContextType,option.ContextId,"Question "+from+" to "+to,"Please clarify"));threads[(from,to)]=id;
   Check(await Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids[to],id,"Authorized reply")),from+" -> "+to+": create and reply with SQL context");
  }
  var supplierBid=(await Service(sp=>sp.GetRequiredService<SupplierCommunicationService>().CreateAsync(ids[SystemRoles.Supplier],new SupplierClarificationInputModel{BidId=bid,Subject="Bid question",Message="Please clarify the submitted offer"})))!.Value;
  Check(await Service(sp=>sp.GetRequiredService<SupplierCommunicationService>().ReplyAsync(ids[SystemRoles.Supplier],supplierBid,"Supplier follow-up")),"supplier bid conversation and portal reply work");
  foreach(var from in roles)foreach(var to in roles){if(ClarificationPolicy.Allowed(from,to))continue;
   bool denied=false;try{await Service(sp=>sp.GetRequiredService<ClarificationService>().CreateAsync(Principal(from),ids[to],"Tender",tender,"Forbidden pair","Must not persist"));}catch(WorkflowException){denied=true;}Check(denied,"forbidden pair refused: "+from+" -> "+to);
  }
  bool badContext=false;try{await Service(sp=>sp.GetRequiredService<ClarificationService>().CreateAsync(Principal(SystemRoles.RequestingDepartment),ids[SystemRoles.SourcingOfficer],"PurchaseRequest",foreignRequest,"Forged context","Invalid"));}catch(WorkflowException){badContext=true;}Check(badContext,"department cannot use another department's request");
  Check(!await Service(sp=>sp.GetRequiredService<SupplierCommunicationService>().CanCreateAsync(ids["SupplierTwo"],bid)),"supplier cannot start conversation about another supplier's bid");
  Check(!await Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids["SupplierTwo"],prebid,"Intrusion")),"unrelated supplier cannot reply");
  Check(!await Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids[SystemRoles.SourcingOfficer],prebid,new string('x',10001))),"oversized reply refused");
  int reassigned=threads[(SystemRoles.SourcingOfficer,SystemRoles.EvaluationCommittee)];
  await Db(async db=>{(await db.CommitteeMembers.SingleAsync(m=>m.Id==evalSeat)).UserId=ids["SupplierTwo"];await db.SaveChangesAsync();});
  Check(!await Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids[SystemRoles.EvaluationCommittee],reassigned,"Lost assignment")),"removed committee assignment blocks further replies");
  await Db(async db=>{(await db.CommitteeMembers.SingleAsync(m=>m.Id==evalSeat)).UserId=ids[SystemRoles.EvaluationCommittee];var u=await db.Users.SingleAsync(u=>u.Id==ids[SystemRoles.TopCommittee]);u.IsActive=false;await db.SaveChangesAsync();});
  Check(!await Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids[SystemRoles.TopCommittee],threads[(SystemRoles.SourcingManager,SystemRoles.TopCommittee)],"Inactive")),"inactive participant cannot continue");
  await Db(async db=>{(await db.Users.SingleAsync(u=>u.Id==ids[SystemRoles.TopCommittee])).IsActive=true;await db.SaveChangesAsync();});
  var clients=new Dictionary<string,HttpClient>();var csrf=new Dictionary<string,string>();
  async Task<string> Token(HttpClient c,string url){var html=await(await c.GetAsync(url)).Content.ReadAsStringAsync();var m=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!m.Success)throw new Exception("No CSRF token "+url);return WebUtility.HtmlDecode(m.Groups[1].Value);}
  foreach(var role in roles.Append("SupplierTwo")){
   var client=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});clients[role]=client;var token=await Token(client,"/Identity/Account/Login");
   var i=Array.IndexOf(roles,role);if(i<0)i=7;
   var login=await client.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","u"+i+"@clarification.invalid"},{"Input.Password",pw},{"__RequestVerificationToken",token}}));Check(login.StatusCode==HttpStatusCode.Redirect,role+": actual Identity login");
   csrf[role]=await Token(client,"/Notifications");
  }
  async Task<HttpResponseMessage> Post(string role,string url,Dictionary<string,string> form){form["__RequestVerificationToken"]=csrf[role];return await clients[role].PostAsync(url,new FormUrlEncodedContent(form));}
  var officer=clients[SystemRoles.SourcingOfficer];var supplier=clients[SystemRoles.Supplier];
  var oldInbox=await officer.GetAsync("/SourcingOfficer/Clarifications");Check(oldInbox.StatusCode==HttpStatusCode.Redirect&&oldInbox.Headers.Location!.ToString().StartsWith("/Clarifications"),"legacy Officer inbox routes to complete shared inbox");
  var inbox=await officer.GetStringAsync("/Clarifications");Check(inbox.Contains("Pre-bid question")&&inbox.Contains("Question Requesting Department"),"Officer inbox shows pre-bid and department threads together");
  var portal=await supplier.GetStringAsync("/Supplier/Clarifications/Details/"+prebid);Check(portal.Contains("Officer pre-bid answer")&&portal.Contains("Mark as resolved"),"Supplier Razor thread shows reply and resolution action");
  var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aM1sAAAAASUVORK5CYII=");int messageId=0,docId=0;
  await Db(async db=>messageId=await db.ClarificationMessages.Where(m=>m.ClarificationId==prebid&&m.SenderUserId==ids[SystemRoles.Supplier]).Select(m=>m.Id).FirstAsync());
  using(var content=new MultipartFormDataContent()){content.Add(new StringContent(messageId.ToString()),"messageId");content.Add(new StringContent(csrf[SystemRoles.Supplier]),"__RequestVerificationToken");content.Add(new ByteArrayContent(png),"file","clarification-test.png");var response=await supplier.PostAsync("/Supplier/Documents/UploadClarification",content);Check(response.StatusCode==HttpStatusCode.Redirect,"actual supplier clarification attachment upload");}
  await Db(async db=>docId=await db.Documents.Where(d=>d.ClarificationMessageId==messageId).Select(d=>d.Id).SingleAsync());
  var download=await officer.GetAsync("/Clarifications/Download/"+docId);Check(download.IsSuccessStatusCode&&(await download.Content.ReadAsByteArrayAsync()).SequenceEqual(png),"Officer downloads exact private clarification attachment");
  Check(!(await clients["SupplierTwo"].GetAsync("/Clarifications/Download/"+docId)).IsSuccessStatusCode,"other supplier cannot download clarification attachment");
  Check(!(await officer.PostAsync("/Clarifications/Reply",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",prebid.ToString()},{"message","No CSRF"}}))).IsSuccessStatusCode,"reply requires antiforgery token");
  Check((await Post(SystemRoles.SourcingOfficer,"/SourcingOfficer/Clarifications/Reply",new(){{"id",prebid.ToString()},{"message","Legacy route response"}})).StatusCode==HttpStatusCode.Redirect,"legacy Officer reply uses shared authorized workflow");
  Check((await Post(SystemRoles.Supplier,"/Supplier/Clarifications/Resolve",new(){{"id",prebid.ToString()}})).StatusCode==HttpStatusCode.Redirect,"supplier resolves own conversation through UI endpoint");
  Check(!await Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids[SystemRoles.SourcingOfficer],prebid,"Closed reply")),"resolved thread blocks replies");
  Check(!await Service(sp=>sp.GetRequiredService<ClarificationService>().ResolveAsync(ids[SystemRoles.Supplier],prebid)),"duplicate resolution refused");
  int race=threads[(SystemRoles.SourcingManager,SystemRoles.TopCommittee)];
  var actions=await Task.WhenAll(Service(sp=>sp.GetRequiredService<ClarificationService>().ReplyAsync(ids[SystemRoles.TopCommittee],race,"Racing reply")),Service(sp=>sp.GetRequiredService<ClarificationService>().ResolveAsync(ids[SystemRoles.SourcingManager],race)));
  Check(actions[1],"concurrent reply/resolve serializes and resolution persists");
  await Db(async db=>{
   Check((await db.Clarifications.SingleAsync(c=>c.Id==prebid)).Status==ClarificationStatus.Closed,"Resolved state persisted in SQL");
   Check(await db.ClarificationMessages.CountAsync(m=>m.ClarificationId==race&&m.Message=="Racing reply")<=1,"concurrency creates at most one racing reply");
   Check(await db.AuditLogs.AnyAsync(a=>a.EntityName=="Clarification"&&a.Action=="ClarificationReplied")&&await db.AuditLogs.AnyAsync(a=>a.EntityName=="Clarification"&&a.Action=="ClarificationResolved"),"reply and resolution audit history persisted");
   Check(await db.Notifications.AnyAsync(n=>n.Title=="Clarification resolved"&&n.UserId==ids[SystemRoles.SourcingOfficer]),"resolution notifies the correct counterparty");
   Check(!await db.Set<AiChatConversation>().AnyAsync(),"human clarification remains separate from AI chat");
  });
  foreach(var client in clients.Values)client.Dispose();
  Console.WriteLine($"RESULT PASS={pass} FAIL=0 SQL_DATABASE={catalog} PRIVATE_TEST_ROOT={files}. SQL and real HTTP tested; no production writes or physical biometrics claimed.");
 }
}
