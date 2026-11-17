using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;

// Temporary real-browser server only; never selects a production database or changes launch profiles.
public static class ThemeBrowserHost
{
 public static async Task Run()
 {
  var connection=Environment.GetEnvironmentVariable("UI_FIXTURE_CONNECTION")!;
  if(!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_UiE2E_Theme_"))throw new InvalidOperationException("Isolated theme database required.");
  var root=Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT")!;
  var addressFile=Environment.GetEnvironmentVariable("THEME_HOST_ADDRESS")!;
  var stopFile=Environment.GetEnvironmentVariable("THEME_HOST_STOP")!;
  Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection",connection);
  using var factory=new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b=>{
   b.UseContentRoot(root).UseEnvironment("Development");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{
    ["ConnectionStrings:Default Connection"]=connection,["DemoData:Enabled"]="false",["TeamAccounts:Enabled"]="false",["TeamDev:Database"]="",["BootstrapAdmin:Email"]="",["BootstrapAdmin:Password"]="",["Logging:LogLevel:Default"]="Warning"}));
  });
  factory.UseKestrel(options=>options.Listen(IPAddress.Loopback,0,listen=>listen.UseHttps()));
  factory.StartServer();
  var addresses=factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
  var address=addresses.Single(x=>x.StartsWith("https://"));
  await File.WriteAllTextAsync(addressFile,address);
  Console.WriteLine("Isolated theme browser host ready: "+address);
  while(!File.Exists(stopFile))await Task.Delay(500);
 }
}
