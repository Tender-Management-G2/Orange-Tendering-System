using Microsoft.Data.SqlClient;

namespace Orange_Tendering_System.Services;

/// <summary>
/// Optional switch to the team's hosted SQL Server (site4now). Used by the launch profile "https (hosted shared database)",
/// which sets <c>HostedDatabase:Enabled = true</c>. The full connection string, including the password, lives only in the
/// developer's User Secrets under <c>ConnectionStrings:Hosted Connection</c>; it is never written to appsettings or source.
///
/// The hosted database is shared by the whole team, so the app does NOT apply EF migrations to it automatically
/// (<c>HostedDatabase:AutoMigrate</c> defaults to false). Pending migrations are reported in the log and applied on purpose
/// with <c>dotnet ef database update --connection ...</c> once the team agrees.
/// </summary>
public static class HostedDatabase
{
    public const string ConnectionName = "Hosted Connection";

    /// <summary>Returns true when this run uses the hosted database.</summary>
    public static bool Apply(WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("HostedDatabase:Enabled")) return false;
        var configured = builder.Configuration.GetConnectionString(ConnectionName);
        const string help = "Run in the web project folder: dotnet user-secrets set \"ConnectionStrings:Hosted Connection\" \"Server=...;Database=...;User Id=...;Password=<your password>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True;\"";
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("The hosted database profile is selected, but ConnectionStrings:Hosted Connection is not set. " + help);

        var connection = new SqlConnectionStringBuilder(configured);
        if (!connection.IntegratedSecurity && (string.IsNullOrWhiteSpace(connection.Password) ||
            connection.Password.Equals("YOUR_PASSWORD", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("ConnectionStrings:Hosted Connection has no real password yet. " + help);

        // In-memory source added last: wins over appsettings, User Secrets and environment variables for this run only.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{TeamDevDatabase.ConnectionName}"] = connection.ConnectionString
        });
        return true;
    }
}
