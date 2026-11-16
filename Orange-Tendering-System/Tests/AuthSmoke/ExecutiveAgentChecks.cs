using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Orange_Tendering_System.Services.Biometric;

public static class ExecutiveAgentChecks
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + ++checks + " " + name); }
    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new Exception("Project root required");
        var supplied = Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION");
        var config = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json").AddUserSecrets<WebAppMarker>(true).Build();
        var cs = new SqlConnectionStringBuilder(supplied ?? config.GetConnectionString("Default Connection")) { InitialCatalog = "OrangeAuthCheck_Executive_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") };
        if (!TeamDevDatabase.IsLocalServer(cs.DataSource)) throw new Exception("Local isolated SQL required");
        var connection = cs.ConnectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        using var app = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b => {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:Default Connection"] = connection, ["TeamDev:Database"] = "", ["DemoData:Enabled"] = "false", ["TeamAccounts:Enabled"] = "false",
                ["BootstrapAdmin:Email"] = "", ["BootstrapAdmin:Password"] = "", ["Logging:LogLevel:Default"] = "Warning", ["Biometric:SupplierPasswordOnly"] = "true",
                ["ExecutiveAgent:BaseUrl"] = "http://localhost:11434", ["ExecutiveAgent:Model"] = "qwen3.5:9b",
                ["ExecutiveAgent:DiagnosticDirectory"] = Path.Combine(root,"App_Data","ExecutiveAgentTest","ollama"),
                // Developer User Secrets may configure local Whisper; this suite asserts the unconfigured state explicitly.
                ["ExecutiveSpeech:WhisperExecutable"] = "", ["ExecutiveSpeech:ModelPath"] = "", ["ExecutiveSpeech:FfmpegExecutable"] = ""
            }));
        });
        var password = "Agent!9" + Convert.ToHexString(RandomNumberGenerator.GetBytes(14));
        string officerId = ""; var ids = new Dictionary<string,string>(); int tenderEn = 0, tenderAr = 0;
        string[] roles = [SystemRoles.Admin, SystemRoles.RequestingDepartment, SystemRoles.SourcingOfficer, SystemRoles.Supplier, SystemRoles.EvaluationCommittee, SystemRoles.SourcingManager, SystemRoles.TopCommittee];
        using (var scope = app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Check(await db.Database.CanConnectAsync() && !await db.Users.AnyAsync(), "new isolated SQL Server database migrated");
            var dept=new Department{Name="Executive test department",IsActive=true};db.Add(dept);await db.SaveChangesAsync();
            foreach(var role in roles)
            {
                var email="agent-"+Array.IndexOf(roles,role)+"@orange.invalid";int? supplierId=null;
                if(role==SystemRoles.Supplier){var company=new Supplier{CompanyName="Isolated agent supplier",Email="supplier@orange.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};db.Add(company);await db.SaveChangesAsync();supplierId=company.Id;}
                var u=new ApplicationUser{Email=email,UserName=email,FullName="Isolated "+role,IsActive=true,EmailConfirmed=true,DepartmentId=dept.Id,SupplierId=supplierId};
                Check((await users.CreateAsync(u,password)).Succeeded&&(await users.AddToRoleAsync(u,role)).Succeeded,"seed isolated "+role);ids[role]=u.Id;
            }
            officerId=ids[SystemRoles.SourcingOfficer];
            var members=new List<(string UserId,CommitteeResponsibility Seat)>{(ids[SystemRoles.SourcingManager],CommitteeResponsibility.SourcingManager),(ids[SystemRoles.EvaluationCommittee],CommitteeResponsibility.DepartmentRepresentative)};
            // Enum member names are selected from the actual first four seats used by the existing Create workflow.
            members[1]=(ids[SystemRoles.EvaluationCommittee],(CommitteeResponsibility)1);
            for(int i=2;i<4;i++){var email=$"extra-evaluator-{i}@orange.invalid";var u=new ApplicationUser{Email=email,UserName=email,FullName=email,IsActive=true,EmailConfirmed=true};await users.CreateAsync(u,password);await users.AddToRoleAsync(u,SystemRoles.EvaluationCommittee);members.Add((u.Id,(CommitteeResponsibility)i));}
            foreach(var title in new[]{"AGENT-EN-TEST","AGENT-AR-TEST"}){
                var pr=new PurchaseRequest{Title=title,Justification="Isolated integration fixture",DepartmentId=dept.Id,CreatedByUserId=ids[SystemRoles.RequestingDepartment],Status=PurchaseRequestStatus.Accepted,CreatedAt=DateTime.UtcNow,Items=[new(){Name="Device",Quantity=1,Unit="each"}]};db.Add(pr);await db.SaveChangesAsync();
                var t=new Tender{Title=title,Description="Isolated Executive Agent integration test",PurchaseRequestId=pr.Id,CreatedByUserId=officerId,CreatedAt=DateTime.UtcNow,Items=[new(){Name="Device",Quantity=1,Unit="each"}]};db.Add(t);await db.SaveChangesAsync();
                db.TenderRounds.Add(new(){TenderId=t.Id,RoundNumber=1,CreatedByUserId=officerId,CreatedAt=DateTime.UtcNow});
                db.CommitteeMembers.AddRange(members.Select(m=>new CommitteeMember{TenderId=t.Id,UserId=m.UserId,CommitteeType=CommitteeType.Evaluation,Responsibility=m.Seat,AssignedAt=DateTime.UtcNow}));await db.SaveChangesAsync();if(title.Contains("-EN-"))tenderEn=t.Id;else tenderAr=t.Id;
            }
        }
        HttpClient Client(){var c=app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});c.Timeout=TimeSpan.FromMinutes(7);return c;}
        async Task<string> Page(HttpClient c,string url){for(var i=0;i<6;i++){var r=await c.GetAsync(url);if(r.Headers.Location!=null){url=r.Headers.Location.ToString();continue;}r.EnsureSuccessStatusCode();return await r.Content.ReadAsStringAsync();}throw new Exception("Too many redirects");}
        async Task<string> Token(HttpClient c,string url="/Identity/Account/Login"){var text=await Page(c,url);var m=Regex.Match(text,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(!m.Success)throw new Exception("CSRF missing: "+url);return WebUtility.HtmlDecode(m.Groups[1].Value);}
        async Task<(HttpClient Client,string Token)> Login(string role){var c=Client();var t=await Token(c);var r=await c.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email","agent-"+Array.IndexOf(roles,role)+"@orange.invalid"},{"Input.Password",password},{"__RequestVerificationToken",t}}));if(role==SystemRoles.Supplier){var rr=await Post(c,t,"/Identity/Account/Login?handler=PasswordOnly",new{});if(!rr.IsSuccessStatusCode)throw new Exception("Supplier sign-in failed");}else Check(r.StatusCode==HttpStatusCode.Redirect,"real Identity login "+role);return(c,await Token(c,"/Workspace"));}
        var officer=await Login(SystemRoles.SourcingOfficer);
        using(var anon=Client())Check((await anon.GetAsync("/ExecutiveAgent/Progress?id="+Guid.NewGuid())).StatusCode==HttpStatusCode.Redirect,"anonymous execution access rejected");
        foreach(var endpoint in new[]{"Run","Confirm","Challenge","Transcribe","Navigate"})
        {
            var missing=await officer.Client.PostAsJsonAsync("/ExecutiveAgent/"+endpoint,new{requestId=Guid.NewGuid(),command="must not execute",actionId=Guid.NewGuid(),confirmed=true});
            var missingBody=await missing.Content.ReadAsStringAsync();Console.WriteLine($"CSRF_HTTP endpoint={endpoint} token=missing status={(int)missing.StatusCode} type={missing.Content.Headers.ContentType} body={missingBody}");
            Check(missing.StatusCode==HttpStatusCode.BadRequest&&missingBody.Contains("antiforgery",StringComparison.OrdinalIgnoreCase),endpoint+": missing CSRF rejected by antiforgery with JSON 400");
            var invalid=await Post(officer.Client,"invalid-token","/ExecutiveAgent/"+endpoint,new{requestId=Guid.NewGuid(),command="must not execute",actionId=Guid.NewGuid(),confirmed=true});
            var invalidBody=await invalid.Content.ReadAsStringAsync();Console.WriteLine($"CSRF_HTTP endpoint={endpoint} token=invalid status={(int)invalid.StatusCode} type={invalid.Content.Headers.ContentType} body={invalidBody}");
            Check(invalid.StatusCode==HttpStatusCode.BadRequest&&invalidBody.Contains("antiforgery",StringComparison.OrdinalIgnoreCase),endpoint+": invalid CSRF rejected by antiforgery with JSON 400");
            var validCsrf=await Post(officer.Client,officer.Token,"/ExecutiveAgent/"+endpoint,new{requestId=Guid.NewGuid(),command="",actionId=Guid.NewGuid(),confirmed=false});
            var validBody=await validCsrf.Content.ReadAsStringAsync();Console.WriteLine($"CSRF_HTTP endpoint={endpoint} token=valid status={(int)validCsrf.StatusCode} type={validCsrf.Content.Headers.ContentType} body={validBody}");
            var correctBusinessValidation=endpoint switch {
                "Run"=>validCsrf.StatusCode==HttpStatusCode.BadRequest&&validBody.Contains("Enter a command"),
                "Confirm"=>validCsrf.StatusCode==HttpStatusCode.NotFound&&validBody.Contains("Action not found"),
                "Challenge"=>validCsrf.StatusCode==HttpStatusCode.Conflict&&validBody.Contains("Pending action not found"),
                "Transcribe"=>validCsrf.StatusCode==HttpStatusCode.BadRequest&&validBody.Contains("Audio is required"),
                "Navigate"=>validCsrf.StatusCode==HttpStatusCode.OK&&validBody.Contains("Unrecognized"),_=>false};
            Check(correctBusinessValidation&&!validBody.Contains("antiforgery",StringComparison.OrdinalIgnoreCase),endpoint+": valid CSRF accepted and actual business validation reached");
        }
        foreach(var role in roles.Where(r=>r!=SystemRoles.Supplier)){
            var session=role==SystemRoles.SourcingOfficer?officer:await Login(role);var page=await Page(session.Client,"/Workspace");
            Check(page.Contains("data-executive-agent")&&page.Contains("data-ai-chat")&&page.Contains("global-search.js"),"preserved guide, search and agent on "+role);
            var catalog=await session.Client.GetFromJsonAsync<JsonElement>("/ExecutiveAgent/Pages");
            Check(catalog.GetArrayLength()>0&&catalog.EnumerateArray().All(p=>p.GetProperty("url").GetString()!.StartsWith('/')&&!p.GetProperty("url").GetString()!.StartsWith("//")),role+": actual registered role-scoped page catalog");
            var expectedArea=role switch{SystemRoles.Admin=>"Admin",SystemRoles.RequestingDepartment=>"Department",SystemRoles.SourcingOfficer=>"SourcingOfficer",SystemRoles.EvaluationCommittee=>"EvaluationCommittee",SystemRoles.SourcingManager=>"SourcingManager",_=>"TopCommittee"};
            var dashboardPage=catalog.EnumerateArray().First(p=>p.GetProperty("area").GetString()==expectedArea&&p.GetProperty("label").GetString()=="Dashboard");
            var navigation=await Post(session.Client,session.Token,"/ExecutiveAgent/Navigate",new{command="افتحلي لوحة التحكم",area=expectedArea});
            var nav=await navigation.Content.ReadFromJsonAsync<JsonElement>();
            Check(navigation.IsSuccessStatusCode&&nav.GetProperty("state").GetString()=="Navigate"&&nav.GetProperty("url").GetString()==dashboardPage.GetProperty("url").GetString(),role+": Arabic direct navigation resolved to real permitted page");
            var destination=await session.Client.GetAsync(nav.GetProperty("url").GetString());
            Check(destination.IsSuccessStatusCode,role+": resolved dashboard GET actually opens");
        }
        foreach(var role in roles)
        {
            using var scope=app.Services.CreateScope();
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var svc=scope.ServiceProvider.GetRequiredService<ExecutiveAgentService>();
            var run=new ExecutiveAgentRun{Id=Guid.NewGuid(),UserId=ids[role],InputHash="knowledge-check",CreatedAt=DateTime.UtcNow};db.Add(run);await db.SaveChangesAsync();
            var guide=JsonSerializer.SerializeToElement(await svc.Dispatch(run.Id,1,"site_help",JsonSerializer.SerializeToElement(new{topic="Explain my workflow"}),Principal(ids[role],[SystemRoles.Admin])),Json);
            Check(guide.GetProperty("roleGuides").GetArrayLength()==1&&guide.GetProperty("roleGuides")[0].GetProperty("role").GetString()==role,role+": actual site_help dispatch rebuilds role from SQL Identity instead of forged Admin claim");
        }
        var forbiddenNav=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Navigate",new{pageId="Admin:Users:Index",area="SourcingOfficer"});
        var deniedNav=await forbiddenNav.Content.ReadFromJsonAsync<JsonElement>();
        Check(deniedNav.GetProperty("state").GetString()=="Unavailable"&&deniedNav.GetProperty("url").ValueKind==JsonValueKind.Null,"Officer cannot navigate to an Admin-only page through a forged page ID");
        var englishId=Guid.NewGuid();
        var en=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Run",new{requestId=englishId,command="Find tender AGENT-EN-TEST, read its current status, then prepare sending its weight request to the evaluation committee. Do not execute or approve anything."});
        var enText=await en.Content.ReadAsStringAsync();if(!en.IsSuccessStatusCode)throw new Exception(enText);Console.WriteLine("MODEL_EN "+enText);
        Guid enAction;
        using(var scope=app.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var steps=await db.AuditLogs.Where(x=>x.EntityId==englishId.ToString()&&x.Action=="AgentToolSucceeded").ToListAsync();
            Check(steps.Any(x=>x.NewValues!.Contains("search_records"))&&steps.Any(x=>x.NewValues!.Contains("tender_status")),"real Qwen English search and status dispatched to SQL-backed services");
            enAction=(await db.ExecutiveAgentActions.SingleAsync(x=>x.RunId==englishId)).Id;Check(!await db.Set<WeightRound>().AnyAsync(x=>x.TenderId==tenderEn),"planning did not mutate workflow");
        }
        Check((await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=enAction,confirmed=false,password})).StatusCode==HttpStatusCode.BadRequest,"unconfirmed action rejected");
        Check((await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=enAction,confirmed=true,password="incorrect"})).StatusCode==HttpStatusCode.Forbidden,"incorrect existing password rejected");
        var executed=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=enAction,confirmed=true,password});Check(executed.IsSuccessStatusCode,"real existing RequestWeights action executed");
        var duplicate=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=enAction,confirmed=true,password});Check((await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicate").GetBoolean(),"duplicate confirmation returns existing result");
        Check((await Post(officer.Client,officer.Token,"/ExecutiveAgent/Run",new{requestId=englishId,command="Find tender AGENT-EN-TEST, read its current status, then prepare sending its weight request to the evaluation committee. Do not execute or approve anything."})).IsSuccessStatusCode,"duplicate command returns persisted result");
        using(var scope=app.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(await db.Set<WeightRound>().CountAsync(x=>x.TenderId==tenderEn)==1,"one real SQL weight request after duplicate attempts");
            Check(await db.Notifications.CountAsync(x=>x.Title=="Evaluation Weight Request")==4,"four actual committee notifications committed");
            Check(await db.AuditLogs.AnyAsync(x=>x.Action=="WeightRequestSent")&&await db.AuditLogs.CountAsync(x=>x.EntityId==englishId.ToString()&&x.Action=="AgentActionRejected")==2&&await db.AuditLogs.AnyAsync(x=>x.Action=="AgentActionSucceeded"),"SQL audit includes real workflow execution, denials and agent receipt");
        }
        var arabicId=Guid.NewGuid();var ar=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Run",new{requestId=arabicId,command="ابحث عن المناقصة AGENT-AR-TEST ثم اعرض حالتها الحالية ثم جهّز طلب الأوزان إلى لجنة التقييم للمراجعة البشرية. لا تنفذ الإجراء ولا تعتمد أي شيء."});
        var arText=await ar.Content.ReadAsStringAsync();if(!ar.IsSuccessStatusCode)throw new Exception(arText);Console.WriteLine("MODEL_AR "+arText);
        using(var scope=app.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Check(await db.ExecutiveAgentActions.AnyAsync(x=>x.RunId==arabicId&&x.TenderId==tenderAr&&x.Operation=="request_weights"),"real Qwen Arabic multi-step prepared correct SQL-backed action");
            var actors=await db.Users.Where(x=>db.CommitteeMembers.Any(m=>m.TenderId==tenderEn&&m.UserId==x.Id)).ToListAsync();
            foreach(var u in actors){var rolesForUser=await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync(u);var p=Principal(u.Id,rolesForUser);await scope.ServiceProvider.GetRequiredService<TenderWorkflowService>().Propose(tenderEn,50,50,p);}
        }
        // Prepare through the same dispatcher, then exercise real Identity WebAuthn verification with a software test authenticator.
        var secureAction=await Prepare(app.Services,officerId,tenderEn,"approve_weights");
        Check((await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=secureAction,confirmed=true,password})).StatusCode==HttpStatusCode.Forbidden,"password cannot authorize protected approval");
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var credentialId=RandomNumberGenerator.GetBytes(32);
        var reg=await(await Post(officer.Client,officer.Token,"/Biometrics/PasskeyRegisterOptions",new{password})).Content.ReadFromJsonAsync<JsonElement>();
        var attestation=Attestation(reg.GetProperty("challenge").GetString()!,credentialId,key);
        Check((await Post(officer.Client,officer.Token,"/Biometrics/PasskeyRegister",new{credentialJson=attestation,label="Isolated software authenticator"})).IsSuccessStatusCode,"existing Identity passkey verifier enrolls isolated test public key");
        async Task<JsonElement> Challenge(Guid action){var r=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Challenge",new{actionId=action});var body=await r.Content.ReadAsStringAsync();if(!r.IsSuccessStatusCode)throw new Exception(body);return JsonDocument.Parse(body).RootElement.GetProperty("options").Clone();}
        var challenge=await Challenge(secureAction);var forged=Assertion(challenge,credentialId,key,officerId,1,true);
        Check((await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=secureAction,confirmed=true,credentialJson=forged})).StatusCode==HttpStatusCode.Forbidden,"forged WebAuthn signature rejected");
        challenge=await Challenge(secureAction);var valid=Assertion(challenge,credentialId,key,officerId,2);
        var approved=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=secureAction,confirmed=true,credentialJson=valid});var approvalText=await approved.Content.ReadAsStringAsync();if(!approved.IsSuccessStatusCode)throw new Exception(approvalText);Check(true,"signed WebAuthn action executes real ApproveWeights service");
        using(var scope=app.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var w=await db.Set<WeightRound>().SingleAsync(x=>x.TenderId==tenderEn);var a=await db.ExecutiveAgentActions.SingleAsync(x=>x.Id==secureAction);
            Check(w.State==WeightState.Approved&&w.ApprovedByUserId==officerId&&w.ApprovedFinancial==50,"approval really persisted and locked in SQL");
            Check(a.WebAuthnAssertionJson!=null&&a.ManifestJson!.Contains(a.SnapshotHash)&&a.ProofChallenge==null,"immutable action manifest and signed proof persisted; challenge consumed");
        }
        var evaluator=await Login(SystemRoles.EvaluationCommittee);Check((await Post(evaluator.Client,evaluator.Token,"/ExecutiveAgent/Confirm",new{actionId=enAction,confirmed=true,password})).StatusCode==HttpStatusCode.NotFound,"another user cannot confirm an action");
        Check((await evaluator.Client.GetAsync("/ExecutiveAgent/Progress?id="+englishId)).StatusCode==HttpStatusCode.NotFound,"another user cannot read run/audit results");
        var invalidAction=await Prepare(app.Services,officerId,tenderAr,"request_weights");
        using(var scope=app.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var t=await db.Tenders.FindAsync(tenderAr);t!.Description+=" changed";await db.SaveChangesAsync();}
        Check((await Post(officer.Client,officer.Token,"/ExecutiveAgent/Confirm",new{actionId=invalidAction,confirmed=true,password})).StatusCode==HttpStatusCode.Conflict,"stale reviewed workflow state rejected");
        using(var scope=app.Services.CreateScope()){
            var svc=scope.ServiceProvider.GetRequiredService<ExecutiveAgentService>();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var run=new ExecutiveAgentRun{Id=Guid.NewGuid(),UserId=ids[SystemRoles.EvaluationCommittee],CreatedAt=DateTime.UtcNow};db.Add(run);await db.SaveChangesAsync();
            try{await svc.Dispatch(run.Id,1,"prepare_action",Args(tenderAr,"request_weights"),Principal(run.UserId,[SystemRoles.EvaluationCommittee]));throw new Exception("Expected permission denial");}catch(AgentException e){Check(e.Status==403,"assigned evaluator cannot perform officer action");}
            try{await svc.Dispatch(run.Id,2,"execute_sql",JsonSerializer.SerializeToElement(new{sql="DROP TABLE Tenders"}),Principal(run.UserId,[SystemRoles.EvaluationCommittee]));throw new Exception("Expected unknown tool denial");}catch(AgentException e){Check(e.Status==403,"unlisted tool never dispatched");}
            var draftArgs=JsonSerializer.SerializeToElement(new{purchaseRequestId=0,title="طارق"});
            try{await svc.Dispatch(run.Id,3,"draft_tender",draftArgs,Principal(run.UserId,[SystemRoles.EvaluationCommittee]));throw new Exception("Expected draft denial");}catch(AgentException e){Check(e.Status==403,"draft_tender denied to non-officer");}
        }
        // draft_tender is a read-only hand-off to the existing New Tender form; it must never create a tender.
        int draftPr;
        using(var scope=app.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var dept=await db.Departments.FirstAsync();
            var pr=new PurchaseRequest{Title="AGENT-DRAFT-PR",Justification="Isolated draft fixture",DepartmentId=dept.Id,CreatedByUserId=ids[SystemRoles.RequestingDepartment],Status=PurchaseRequestStatus.Accepted,CreatedAt=DateTime.UtcNow,Items=[new(){Name="Laptop",Quantity=2,Unit="each"}]};
            db.Add(pr);await db.SaveChangesAsync();draftPr=pr.Id;
        }
        int tendersBefore;using(var scope=app.Services.CreateScope())tendersBefore=await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Tenders.CountAsync();
        var draftId=Guid.NewGuid();var draft=await Post(officer.Client,officer.Token,"/ExecutiveAgent/Run",new{requestId=draftId,command="انشئ عطاء باسم طارق"});
        var draftText=await draft.Content.ReadAsStringAsync();Console.WriteLine("MODEL_DRAFT "+draftText);
        using(var scope=app.Services.CreateScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draftStep=await db.AuditLogs.Where(x=>x.EntityId==draftId.ToString()&&x.Action=="AgentToolSucceeded"&&x.NewValues!.Contains("draft_tender")).Select(x=>x.NewValues).FirstOrDefaultAsync();
            Check(draft.IsSuccessStatusCode&&draftStep!=null,"real Qwen Arabic 'create tender' dispatched draft_tender");
            Check(draftStep!.Contains("FormReady")&&draftStep.Contains($"/SourcingOfficer/Workflow?request={draftPr}")&&draftStep.Contains("title="),"draft_tender opened the existing form for the only eligible request with the title");
            Check(await db.Tenders.CountAsync()==tendersBefore&&!await db.ExecutiveAgentActions.AnyAsync(x=>x.RunId==draftId),"draft_tender created no tender and no pending action");
        }
        // Local speech adapter availability must be honest when no runtime/model has been configured.
        var speech=await officer.Client.GetFromJsonAsync<JsonElement>("/ExecutiveAgent/SpeechStatus");Check(!speech.GetProperty("available").GetBoolean(),"unconfigured local STT reports unavailable rather than cloud/fake success");
        Console.WriteLine($"RESULT PASS={checks} FAIL=0 DATABASE={cs.InitialCatalog}. Real local Qwen, SQL Server, Identity and workflow services. No browser microphone/audio was supplied; WebAuthn used an isolated software test authenticator, not hardware biometrics.");
        // A guarded browser-host helper can reuse this explicitly isolated database later; credentials remain local and untracked.
        var dataFolder=Path.Combine(root,"App_Data","ExecutiveAgentTest");Directory.CreateDirectory(dataFolder);
        await File.WriteAllTextAsync(Path.Combine(dataFolder,"session.json"),JsonSerializer.Serialize(new{connection,password,email="agent-2@orange.invalid",tenderId=tenderAr,database=cs.InitialCatalog},Json));
    }
    static ClaimsPrincipal Principal(string id,IEnumerable<string> roles)=>new(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,id)}.Concat(roles.Select(r=>new Claim(ClaimTypes.Role,r))),"isolated-test"));
    static JsonElement Args(int id,string operation)=>JsonSerializer.SerializeToElement(new{tenderId=id,operation,comment="Isolated test reviewed action",decision=""});
    static async Task<Guid> Prepare(IServiceProvider services,string user,int tender,string op){using var scope=services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var run=new ExecutiveAgentRun{Id=Guid.NewGuid(),UserId=user,CreatedAt=DateTime.UtcNow};db.Add(run);await db.SaveChangesAsync();var value=await scope.ServiceProvider.GetRequiredService<ExecutiveAgentService>().Dispatch(run.Id,1,"prepare_action",Args(tender,op),Principal(user,[SystemRoles.SourcingOfficer]));return JsonSerializer.SerializeToElement(value,Json).GetProperty("actionId").GetGuid();}
    static Task<HttpResponseMessage> Post(HttpClient c,string token,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body,options:Json)};r.Headers.Add("RequestVerificationToken",token);return c.SendAsync(r);}
    static byte[] Auth(byte flags,uint count,byte[]? attested=null){var bytes=new List<byte>(SHA256.HashData(Encoding.UTF8.GetBytes("localhost"))){flags};bytes.AddRange([(byte)(count>>24),(byte)(count>>16),(byte)(count>>8),(byte)count]);if(attested!=null)bytes.AddRange(attested);return bytes.ToArray();}
    static byte[] Cbor(int major,byte[] bytes){var head=bytes.Length<24?new[]{(byte)(major<<5|bytes.Length)}:bytes.Length<256?new[]{(byte)(major<<5|24),(byte)bytes.Length}:new[]{(byte)(major<<5|25),(byte)(bytes.Length>>8),(byte)bytes.Length};return head.Concat(bytes).ToArray();}
    static byte[] CborText(string text)=>Cbor(3,Encoding.UTF8.GetBytes(text));
    static string Attestation(string challenge,byte[] id,ECDsa key){var p=key.ExportParameters(false);var cose=new List<byte>{0xA5,0x01,0x02,0x03,0x26,0x20,0x01,0x21,0x58,0x20};cose.AddRange(p.Q.X!);cose.AddRange([0x22,0x58,0x20]);cose.AddRange(p.Q.Y!);var a=new List<byte>(new byte[16]){(byte)(id.Length>>8),(byte)id.Length};a.AddRange(id);a.AddRange(cose);var data=Auth(0x45,0,a.ToArray());var o=new List<byte>{0xA3};o.AddRange(CborText("fmt"));o.AddRange(CborText("none"));o.AddRange(CborText("attStmt"));o.Add(0xA0);o.AddRange(CborText("authData"));o.AddRange(Cbor(2,data));var client=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{type="webauthn.create",challenge,origin="https://localhost",crossOrigin=false}));return JsonSerializer.Serialize(new{id=Base64Url.Encode(id),rawId=Base64Url.Encode(id),type="public-key",response=new{clientDataJSON=Base64Url.Encode(client),attestationObject=Base64Url.Encode(o.ToArray()),transports=new[]{"internal"}},clientExtensionResults=new{}});}
    static string Assertion(JsonElement options,byte[] id,ECDsa key,string user,uint counter,bool tamper=false){var cd=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{type="webauthn.get",challenge=options.GetProperty("challenge").GetString(),origin="https://localhost",crossOrigin=false}));var auth=Auth(0x05,counter);var signature=key.SignData(auth.Concat(SHA256.HashData(cd)).ToArray(),HashAlgorithmName.SHA256,DSASignatureFormat.Rfc3279DerSequence);if(tamper)signature[^1]^=0x01;return JsonSerializer.Serialize(new{id=Base64Url.Encode(id),rawId=Base64Url.Encode(id),type="public-key",response=new{clientDataJSON=Base64Url.Encode(cd),authenticatorData=Base64Url.Encode(auth),signature=Base64Url.Encode(signature),userHandle=Base64Url.Encode(Encoding.UTF8.GetBytes(user))},clientExtensionResults=new{}});}
}
