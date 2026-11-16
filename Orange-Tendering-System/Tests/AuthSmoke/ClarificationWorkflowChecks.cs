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
    if(role is SystemRoles.Supplier or "SupplierTwo"){db.FaceTemplates.Add(new FaceTemplate{UserId=u.Id,ProtectedTemplate=[1],CreatedAt=DateTime.UtcNow});await db.SaveChangesAsync();}
   }
   var pr=new PurchaseRequest{Title="Clarification request",DepartmentId=d.Id,CreatedByUserId=ids[SystemRoles.RequestingDepartment],Status=PurchaseRequestStatus.Accepted,Justification="SQL fixture"};
   var foreign=new PurchaseRequest{Title="Other department request",DepartmentId=other.Id,CreatedByUserId=ids[SystemRoles.SourcingOfficer],Status=PurchaseRequestStatus.Accepted,Justification="SQL fixture"};db.AddRange(pr,foreign);await db.SaveChangesAsync();request=pr.Id;foreignRequest=foreign.Id;
   var t=new Tender{Title="Clarification tender",PurchaseRequestId=pr.Id,CreatedByUserId=ids[SystemRoles.SourcingOfficer],Status=TenderStatus.Bidding};db.Add(t);await db.SaveChangesAsync();tender=t.Id;
   var round=new TenderRound{TenderId=t.Id,RoundNumber=1,Status=TenderRoundStatus.Open,OpensAt=DateTime.UtcNow.AddDays(-1),Deadline=DateTime.UtcNow.AddDays(1),CreatedByUserId=ids[SystemRoles.SourcingOfficer]};db.Add(round);await db.SaveChangesAsync();
   var invite=new TenderInvitation{TenderRoundId=round.Id,SupplierId=companies[SystemRoles.Supplier],InvitedByUserId=ids[SystemRoles.SourcingOfficer],InvitedAt=DateTime.UtcNow};db.Add(invite);await db.SaveChangesAsync();invitation=invite.Id;
   var evaluator=new CommitteeMember{TenderId=t.Id,UserId=ids[SystemRoles.EvaluationCommittee],CommitteeType=CommitteeType.Evaluation,Responsibility=CommitteeResponsibility.TechnicalRepresentative};db.Add(evaluator);
   db.AddRange(new CommitteeMember{TenderId=t.Id,UserId=ids[SystemRoles.SourcingManager],CommitteeType=CommitteeType.Evaluation,Responsibility=CommitteeResponsibility.SourcingManager},new CommitteeMember{TenderId=t.Id,UserId=ids[SystemRoles.TopCommittee],CommitteeType=CommitteeType.Top});await db.SaveChangesAsync();evalSeat=evaluator.Id;
  });
  var beforeBidOptions=await Service(sp=>sp.GetRequiredService<ClarificationService>().Options(Principal(SystemRoles.SourcingOfficer)));
  var invitationOption=beforeBidOptions.Single(o=>o.RecipientId==ids[SystemRoles.Supplier]&&o.ContextType=="Tender"&&o.ContextId==tender);
  Check(!beforeBidOptions.Any(o=>o.RecipientId==ids["SupplierTwo"]),"uninvited supplier is not offered as a contact");
  var officerPrebid=await Service(sp=>sp.GetRequiredService<ClarificationService>().CreateAsync(Principal(SystemRoles.SourcingOfficer),invitationOption.RecipientId,invitationOption.ContextType,invitationOption.ContextId,"Officer invitation question","Please clarify before bidding"));
  Check(await Service(sp=>sp.GetRequiredService<SupplierCommunicationService>().ReplyAsync(ids[SystemRoles.Supplier],officerPrebid,"Supplier pre-bid answer")),"Officer starts invitation-linked chat before bidding and Supplier replies");
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
  async Task<string> Token(HttpClient c,string url){var response=await c.GetAsync(url);var html=await response.Content.ReadAsStringAsync();var m=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!m.Success)throw new Exception("No CSRF token "+url+" status="+response.StatusCode+" location="+response.Headers.Location);return WebUtility.HtmlDecode(m.Groups[1].Value);}
  foreach(var role in roles.Append("SupplierTwo")){
   var client=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});clients[role]=client;var token=await Token(client,"/Identity/Account/Login");
   var i=Array.IndexOf(roles,role);if(i<0)i=7;
   var login=await client.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","u"+i+"@clarification.invalid"},{"Input.Password",pw},{"__RequestVerificationToken",token}}));
   if(role is SystemRoles.Supplier or "SupplierTwo"){Check(login.StatusCode==HttpStatusCode.OK,role+": password accepted before identity step");var step=await client.PostAsync("/Identity/Account/Login?handler=PasswordOnly",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));Check(step.IsSuccessStatusCode && (await step.Content.ReadAsStringAsync()).Contains("signedIn"),role+": password-only identity step completed");}
   else Check(login.StatusCode==HttpStatusCode.Redirect,role+": actual Identity login");
   csrf[role]=await Token(client, role == SystemRoles.Supplier || role == "SupplierTwo" ? "/Supplier/Clarifications" : "/Clarifications");
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
  // Sourcing Manager: the legacy area URLs now use the one shared, role-checked chat.
  var manager=clients[SystemRoles.SourcingManager];
  var managerOld=await manager.GetAsync("/SourcingManager/Clarifications");Check(managerOld.StatusCode==HttpStatusCode.Redirect&&managerOld.Headers.Location!.ToString().StartsWith("/Clarifications"),"legacy Manager inbox routes to shared role-based inbox");
  // New clarification: a checklist of exactly the people SQL data allows; ticking several sends one message to each in its own thread.
  var officerOptions=await Service(sp=>sp.GetRequiredService<ClarificationService>().Options(Principal(SystemRoles.SourcingOfficer)));
  var oldNew=await officer.GetAsync("/Clarifications/New");Check(oldNew.StatusCode==HttpStatusCode.Redirect&&oldNew.Headers.Location!.ToString().Contains("compose=1"),"old New link opens the Create window on the list");
  var officerNew=await officer.GetStringAsync("/Clarifications?compose=1");
  var boxes=System.Text.RegularExpressions.Regex.Matches(officerNew,"name=\"options\"").Count;
  Check(boxes==officerOptions.Count&&boxes>=2,"New clarification lists exactly the people allowed by SQL data ("+boxes+")");
  var picked=officerOptions.Take(2).ToList();int before=0,after=0;
  await Db(async db=>before=await db.Clarifications.CountAsync(c=>c.FromUserId==ids[SystemRoles.SourcingOfficer]));
  var form=new List<KeyValuePair<string,string>>{new("__RequestVerificationToken",csrf[SystemRoles.SourcingOfficer]),new("message","Checklist message to several people\nSecond line")};
  form.AddRange(picked.Select(o=>new KeyValuePair<string,string>("options",o.Value)));
  var multi=await officer.PostAsync("/Clarifications/New",new FormUrlEncodedContent(form));
  await Db(async db=>after=await db.Clarifications.CountAsync(c=>c.FromUserId==ids[SystemRoles.SourcingOfficer]&&c.Subject=="Checklist message to several people"));
  Check(multi.StatusCode==HttpStatusCode.Redirect&&after==picked.Count,"ticking several people creates one conversation each with the message as subject");
  var none=await Post(SystemRoles.SourcingOfficer,"/Clarifications/New",new(){{"message","Nobody chosen"}});
  Check(none.StatusCode==HttpStatusCode.Redirect&&none.Headers.Location!.ToString().Contains("compose=1"),"Create without a ticked person is refused");
  int threadsBefore=0,threadsAfter=0;await Db(async db=>threadsBefore=await db.Clarifications.CountAsync());
  var onlyPending=await Post(SystemRoles.SourcingOfficer,"/Clarifications/New",new(){{"pending",ids[SystemRoles.SourcingManager]},{"message","Ticked without a shared record"}});
  await Db(async db=>threadsAfter=await db.Clarifications.CountAsync());
  Check(onlyPending.StatusCode==HttpStatusCode.Redirect&&onlyPending.Headers.Location!.ToString().Contains("compose=1")&&threadsAfter==threadsBefore,"ticked person without a shared record (or forged id) creates no conversation");
  var mixedForm=new List<KeyValuePair<string,string>>{new("__RequestVerificationToken",csrf[SystemRoles.SourcingOfficer]),new("message","Mixed selection"),new("options",officerOptions[0].Value),new("pending",ids[SystemRoles.Admin])};
  var mixed=await officer.PostAsync("/Clarifications/New",new FormUrlEncodedContent(mixedForm));
  await Db(async db=>threadsAfter=await db.Clarifications.CountAsync(c=>c.Subject=="Mixed selection"));
  Check(mixed.StatusCode==HttpStatusCode.Redirect&&threadsAfter==1,"mixed selection creates only the linked conversation");
  foreach(var who in new[]{SystemRoles.RequestingDepartment,SystemRoles.SourcingOfficer,SystemRoles.EvaluationCommittee,SystemRoles.SourcingManager})
  {
   var svc=await Service(async sp=>{var s=sp.GetRequiredService<ClarificationService>();var o=await s.Options(Principal(who));return (o,await s.PendingContacts(Principal(who),o));});
   Check(svc.Item2.All(p=>ClarificationPolicy.Partners(who).Contains(p.Role))&&!svc.Item2.Any(p=>svc.Item1.Any(o=>o.Recipient==p.Name)),who+": 'not available yet' lists only partner roles without a shared record");
  }
  var managerNew=await manager.GetStringAsync("/Clarifications?compose=1");Check(managerNew.Contains("Create clarification")&&managerNew.Contains("name=\"message\""),"Manager opens the Create window next to the table");
  // Unread replies: the recipient sees an orange "new" badge and counter; opening the conversation marks it read.
  var unreadThread=0;string? unreadUser=null;
  await Db(async db=>{var n=await db.Notifications.Where(x=>!x.IsRead&&x.Link!=null&&x.Link.StartsWith("/Clarifications/Details/")&&x.UserId==ids[SystemRoles.SourcingOfficer]).OrderByDescending(x=>x.Id).FirstOrDefaultAsync();if(n!=null){unreadThread=int.Parse(n.Link!["/Clarifications/Details/".Length..]);unreadUser=n.UserId;}});
  Check(unreadThread>0,"officer has an unread clarification notification to test with");
  var inboxUnread=await officer.GetStringAsync("/Clarifications");Check(inboxUnread.Contains("cc-new-badge")&&inboxUnread.Contains("is-alert"),"unread replies show the orange card and a 'new' badge");
  await officer.GetStringAsync("/Clarifications/Details/"+unreadThread);
  var stillUnread=0;await Db(async db=>stillUnread=await db.Notifications.CountAsync(x=>x.UserId==unreadUser&&!x.IsRead&&x.Link=="/Clarifications/Details/"+unreadThread));
  Check(stillUnread==0,"opening the conversation marks its notifications read");
  var mainStart=managerNew.IndexOf("id=\"workspace-main\"");Check(mainStart>0&&managerNew.IndexOf("clarification-chat.css",mainStart)>mainStart,"chat styles load inside page content (pop-up safe)");
  var managerThread=threads.TryGetValue((SystemRoles.SourcingManager,SystemRoles.EvaluationCommittee),out var mt)?mt:threads[(SystemRoles.EvaluationCommittee,SystemRoles.SourcingManager)];
  var managerPage=await manager.GetStringAsync("/Clarifications/Details/"+managerThread);Check(managerPage.Contains("Clarification Chat")&&managerPage.Contains("Sourcing Manager")&&managerPage.Contains("Evaluation Committee"),"Manager sees the role-based chat thread");
  var managerMayReply=await Service(async sp=>{var t=await sp.GetRequiredService<ApplicationDbContext>().Clarifications.SingleAsync(c=>c.Id==managerThread);return await sp.GetRequiredService<ClarificationService>().CanContinue(t);});
  var managerReply=await Post(SystemRoles.SourcingManager,"/SourcingManager/Clarifications/Reply",new(){{"id",managerThread.ToString()},{"message","Manager legacy reply"}});
  Check(managerMayReply?managerReply.StatusCode==HttpStatusCode.Redirect:!managerReply.IsSuccessStatusCode&&managerReply.StatusCode!=HttpStatusCode.Redirect,"legacy Manager reply follows the shared policy");
  var foreignReply=await Post(SystemRoles.SourcingManager,"/SourcingManager/Clarifications/Reply",new(){{"id",prebid.ToString()},{"message","Not my conversation"}});
  var foreignSaved=await Service(sp=>sp.GetRequiredService<ApplicationDbContext>().ClarificationMessages.AnyAsync(m=>m.ClarificationId==prebid&&m.Message=="Not my conversation"));
  // A refused POST is re-executed by the status-code error page, so only "not a success/redirect, nothing saved" is asserted.
  Check(!foreignReply.IsSuccessStatusCode&&foreignReply.StatusCode!=HttpStatusCode.Redirect&&!foreignSaved,"Manager cannot reply in an Officer-Supplier conversation");
  Check((await Post(SystemRoles.SourcingManager,"/SourcingManager/Clarifications/Create",new(){{"tenderId",tender.ToString()},{"toUserId",ids[SystemRoles.Supplier]},{"subject","Forbidden"},{"message","Manager to supplier"}})).Headers.Location?.ToString().Contains("/Clarifications/New")==true,"legacy Manager create refuses an unauthorized role");
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
