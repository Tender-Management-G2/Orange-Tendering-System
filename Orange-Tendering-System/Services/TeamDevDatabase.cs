using Microsoft.Data.SqlClient;

namespace Orange_Tendering_System.Services;

/// <summary>
/// The isolated team-development database and the boundary for anything seeded with fixed passwords.
///
/// The Development launch profiles set <c>TeamDev:Database = OrangeTenderingTeamDev</c>. In Development only, that name
/// replaces the database (Initial Catalog) of the developer's own "Default Connection" — their SQL Server instance and
/// authentication settings are kept, nothing is written to configuration files or User Secrets. Existing databases such
/// as OrangeTenderingSystem are not touched; the "https (existing OrangeTenderingSystem database)" profile still opens them.
///
/// Fixed-password seeding (team accounts, optional demo fixtures) runs only when the environment is Development AND the
/// effective database is a local team database: name "OrangeTenderingTeamDev" or "OrangeTenderingTeamDev_*" on a local
/// SQL Server. Test runs that pass an explicit connection string (for example OrangeAuthCheck_*) are left as they are.
/// </summary>
public static class TeamDevDatabase
{
    public const string DefaultName = "OrangeTenderingTeamDev";
    public const string ConnectionName = "Default Connection";

    /// <summary>Selects the team database for a Development run when the launch profile asks for it.</summary>
    public static void Apply(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment()) return;
        var name = builder.Configuration["TeamDev:Database"]?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (!IsTeamDatabaseName(name))
            throw new InvalidOperationException($"TeamDev:Database must be '{DefaultName}' or start with '{DefaultName}_' (was '{name}').");

        var configured = builder.Configuration.GetConnectionString(ConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionName}' not found.");
        var connection = new SqlConnectionStringBuilder(configured) { InitialCatalog = name };
        // In-memory source added last: it wins over appsettings, User Secrets and environment variables for this run only.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ConnectionName}"] = connection.ConnectionString
        });
    }

    public static bool IsTeamDatabaseName(string? name) =>
        !string.IsNullOrEmpty(name) &&
        (name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase) || name.StartsWith(DefaultName + "_", StringComparison.OrdinalIgnoreCase));

    /// <summary>True only for a team database on this machine's SQL Server.</summary>
    public static bool IsLocalTeamDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        var connection = new SqlConnectionStringBuilder(connectionString);
        return IsTeamDatabaseName(connection.InitialCatalog) && IsLocalServer(connection.DataSource);
    }

    /// <summary>True for this machine's SQL Server (localhost, ., (local), LocalDB, loopback or the machine name).</summary>
    public static bool IsLocalServer(string? dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource)) return false;
        var host = dataSource.Trim();
        if (host.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        if (host.StartsWith("np:", StringComparison.OrdinalIgnoreCase) || host.StartsWith("lpc:", StringComparison.OrdinalIgnoreCase))
            host = host[(host.IndexOf(':') + 1)..];
        if (host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)) return true;
        host = host.Split(',')[0].Split('\\')[0].Trim();
        return host is "." or "(local)" or "127.0.0.1" or "::1" ||
               host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Server and database only (never credentials), for diagnostics.</summary>
    public static string Describe(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "(none)";
        var connection = new SqlConnectionStringBuilder(connectionString);
        return $"{connection.InitialCatalog} on {connection.DataSource}";
    }
}
