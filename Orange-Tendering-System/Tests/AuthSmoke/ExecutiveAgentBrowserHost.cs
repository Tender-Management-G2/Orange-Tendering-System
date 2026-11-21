using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Orange_Tendering_System;
using Orange_Tendering_System.Services;

public static class ExecutiveAgentBrowserHost
{
    public static async Task Run()
    {
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new Exception("AUTH_PROJECT_ROOT required");
        using var session = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root,"App_Data","ExecutiveAgentTest","session.json")));
        var connection=session.RootElement.GetProperty("connection").GetString()!;
        var cs=new SqlConnectionStringBuilder(connection);
        if(!cs.InitialCatalog.StartsWith("OrangeAuthCheck_Executive_",StringComparison.Ordinal)||!TeamDevDatabase.IsLocalServer(cs.DataSource))throw new Exception("Isolated local Executive test database required");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);
        using var app=new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b=>{
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
                ["ConnectionStrings:Default Connection"]=connection,["TeamDev:Database"]="",["DemoData:Enabled"]="false",["TeamAccounts:Enabled"]="false",
                ["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]="",["Logging:LogLevel:Default"]="Warning",["Biometric:SupplierPasswordOnly"]="true"
            }));
        });
        // Repository's fixed application ports. An occupied port fails; no alternate copy/port is launched.
        app.UseKestrel(options=>{options.Listen(IPAddress.Loopback,5254);options.Listen(IPAddress.Loopback,7182,listen=>listen.UseHttps());});
        app.StartServer();
        Console.WriteLine("ISOLATED_BROWSER_READY https://localhost:7182/Identity/Account/Login DATABASE="+cs.InitialCatalog);
        Console.WriteLine("Isolated test login: "+session.RootElement.GetProperty("email").GetString()+" (Sourcing Officer). Choose the 'Other User' tab on the sign-in page; the default 'Supplier' tab rejects this account.");
        Console.WriteLine("Password is stored only in ignored App_Data/ExecutiveAgentTest/session.json for this isolated fixture.");
        Console.WriteLine("Ctrl+C stops the test host. No production/team database is used.");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
