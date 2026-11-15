using System.Net;
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

public static class RetenderChecks
{
    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("AUTH_TEST_CONNECTION")!;
        var name = new SqlConnectionStringBuilder(connection).InitialCatalog;
        if (!name.StartsWith("OrangeAuthCheck_Retender_")) throw new InvalidOperationException("Fresh isolated re-tender SQL database required.");
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("DemoData__Enabled", "false");
        using var app = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b => {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:Default Connection"]=connection,["DemoData:Enabled"]="false",
                ["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]="" })); });
        void Check(bool ok,string label) { if(!ok)throw new InvalidOperationException("FAIL "+label);Console.WriteLine("PASS "+label); }
        HttpClient Client()=>app.CreateClient(new(){BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false});
        var password="T!9a"+Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string officerId, otherId; int tenderId,supplierId,oldBid;
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Check(!await db.Users.AnyAsync(),"isolated database initially empty");
            var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            async Task<ApplicationUser> User(string email,string role) {
                var u=new ApplicationUser{UserName=email,Email=email,EmailConfirmed=true,IsActive=true,FullName="Isolated re-tender check"};
                Check((await users.CreateAsync(u,password)).Succeeded,"Identity account created: "+role);
                Check((await users.AddToRoleAsync(u,role)).Succeeded,"Identity role assigned: "+role);return u; }
            var officer=await User("officer@retender.invalid",SystemRoles.SourcingOfficer);officerId=officer.Id;
            otherId=(await User("other@retender.invalid",SystemRoles.EvaluationCommittee)).Id;
            var d=new Department{Name="Re-tender isolated department"};db.Add(d);await db.SaveChangesAsync();
            var request=new PurchaseRequest{DepartmentId=d.Id,CreatedByUserId=officerId,Title="Retender fixture",Justification="Isolated SQL HTTP checks",Status=PurchaseRequestStatus.Accepted};db.Add(request);await db.SaveChangesAsync();
            var t=new Tender{PurchaseRequestId=request.Id,Title="Retender fixture",CreatedByUserId=officerId,Status=TenderStatus.Bidding,CreatedAt=DateTime.UtcNow};db.Add(t);await db.SaveChangesAsync();tenderId=t.Id;
            var supplier=new Supplier{CompanyName="Preserved supplier",Email="company@retender.invalid",RegistrationNumber=Guid.NewGuid().ToString(),Status=SupplierStatus.Approved};db.Add(supplier);await db.SaveChangesAsync();supplierId=supplier.Id;
            var round=new TenderRound{TenderId=t.Id,RoundNumber=1,Status=TenderRoundStatus.Closed,Deadline=DateTime.UtcNow.AddMinutes(-1),CreatedByUserId=officerId,CreatedAt=DateTime.UtcNow};db.Add(round);await db.SaveChangesAsync();
            var invitation=new TenderInvitation{TenderRoundId=round.Id,SupplierId=supplier.Id,InvitedByUserId=officerId,InvitedAt=DateTime.UtcNow};db.Add(invitation);await db.SaveChangesAsync();
            var bid=new Bid{TenderInvitationId=invitation.Id,Status=BidStatus.Submitted,CreatedAt=DateTime.UtcNow};db.Add(bid);
            db.Add(new WeightRound{TenderId=t.Id,Version=1,State=WeightState.Approved,ApprovedFinancial=40,ApprovedTechnical=60,ApprovedByUserId=officerId,ApprovedAt=DateTime.UtcNow,RequirementsJson="{}"});await db.SaveChangesAsync();oldBid=bid.Id;
        }
        async Task<string> Token(HttpClient c,string url) {
            var text=await (await c.GetAsync(url)).Content.ReadAsStringAsync();
            var m=Regex.Match(text,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");Check(m.Success,"antiforgery token available");return WebUtility.HtmlDecode(m.Groups[1].Value); }
        async Task Login(HttpClient c,string email) {
            var token=await Token(c,"/Identity/Account/Login");var r=await c.PostAsync("/Identity/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>{{"Input.Email",email},{"Input.Password",password},{"__RequestVerificationToken",token}}));Check(r.StatusCode==HttpStatusCode.Redirect,"real Identity login"); }
        async Task<HttpResponseMessage> Post(HttpClient c,string token,string reason)=>await c.PostAsync("/SourcingOfficer/Tenders/StartNextRound",new FormUrlEncodedContent(new Dictionary<string,string>{{"tenderId",tenderId.ToString()},{"reason",reason},{"__RequestVerificationToken",token}}));
        using var anon=Client();var at=await Token(anon,"/Identity/Account/Login");Check((await Post(anon,at,"Unauthorized attempt")).StatusCode==HttpStatusCode.Redirect,"anonymous direct POST denied");
        using var wrong=Client();await Login(wrong,"other@retender.invalid");var wt=await Token(wrong,"/EvaluationCommittee");var denied=await Post(wrong,wt,"Wrong role");Check(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden,"wrong-role direct POST denied");
        using var client=Client();await Login(client,"officer@retender.invalid");var token=await Token(client,$"/SourcingOfficer/Tenders/Rounds/{tenderId}");
        Check((await Post(client,"","Missing CSRF")).StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.MethodNotAllowed,"antiforgery cannot be bypassed");
        Check((await Post(client,token,"")).StatusCode==HttpStatusCode.BadRequest,"mandatory reason enforced");
        var results=await Task.WhenAll(Post(client,token,"First concurrent retry"),Post(client,token,"Second concurrent retry"));
        Check(results.Count(r=>r.StatusCode==HttpStatusCode.Redirect)==1,"concurrent requests create one draft round only");
        async Task Db(Func<ApplicationDbContext,Task> action){using var sc=app.Services.CreateScope();await action(sc.ServiceProvider.GetRequiredService<ApplicationDbContext>());}
        await Db(async db=>{Check(await db.TenderRounds.CountAsync(r=>r.TenderId==tenderId)==2,"exactly two rounds persisted");Check(await db.Bids.AnyAsync(b=>b.Id==oldBid),"previous bid preserved");Check(await db.Suppliers.AnyAsync(s=>s.Id==supplierId),"supplier preserved");Check(await db.Set<RoundDecision>().CountAsync(r=>r.Kind==RoundDecisionKind.Retender)==1,"one official decision and audit transaction");Check(await db.AuditLogs.AnyAsync(a=>a.Action=="RetenderEvidence"),"shared re-tender evidence recorded");});
        using(var sc=app.Services.CreateScope()) {var w=sc.ServiceProvider.GetRequiredService<Orange_Tendering_System.Services.TenderWorkflowService>();await w.OpenBidding(tenderId,DateTimeOffset.UtcNow.AddDays(1),new(new System.Security.Claims.ClaimsIdentity([new(System.Security.Claims.ClaimTypes.NameIdentifier,officerId)],"test")));}
        await Db(async db=>{Check(await db.TenderInvitations.CountAsync(i=>i.TenderRound.TenderId==tenderId)==2,"opening new round carries forward supplier invitation without deleting old invitation");});
        Check((await Post(client,token,"Open round bypass")).StatusCode==HttpStatusCode.BadRequest,"open round direct POST cannot bypass closure");
        // Set up closed-round fixture state only in this isolated SQL database.
        for(int round=2;round<=3;round++) {await Db(async db=>{var r=await db.TenderRounds.SingleAsync(r=>r.TenderId==tenderId&&r.RoundNumber==round);r.Status=TenderRoundStatus.Closed;await db.SaveChangesAsync();});Check((await Post(client,token,"Next permitted retry")).StatusCode==HttpStatusCode.Redirect,"permitted retry "+round);}
        await Db(async db=>{var r=await db.TenderRounds.SingleAsync(r=>r.TenderId==tenderId&&r.RoundNumber==4);r.Status=TenderRoundStatus.Closed;await db.SaveChangesAsync();});
        var fifth=await Post(client,token,"Forbidden fifth round");Check(fifth.StatusCode==HttpStatusCode.BadRequest && (await fifth.Content.ReadAsStringAsync()).Contains("Round 5"),"fifth-round direct HTTP request rejected");
        await Db(async db=>{Check(await db.TenderRounds.CountAsync(r=>r.TenderId==tenderId)==4,"SQL remains exactly four rounds");Check(await db.Bids.AnyAsync(b=>b.Id==oldBid),"historical bid remains after all retries");});
        Console.WriteLine("Re-tender SQL Server HTTP checks passed; isolated database retained: "+name);
    }
}
