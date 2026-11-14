

// sara rjoub 



using Microsoft.Data.SqlClient;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services
{
    public class SettingsService
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public SettingsService(
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        public SettingsViewModel GetSettings()
        {
            var connectionString = _configuration
                .GetConnectionString("Default Connection");

            string databaseServer = "Not configured";
            string databaseName = "Not configured";

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                try
                {
                    var connection = new SqlConnectionStringBuilder(
                        connectionString);

                    databaseServer = connection.DataSource;
                    databaseName = connection.InitialCatalog;
                }
                catch (ArgumentException)
                {
                    databaseServer = "Invalid configuration";
                    databaseName = "Invalid configuration";
                }
            }

            // Read only declared application configuration.
            // These values do not prove a service is operational.
            var emailConfigured =
                _configuration.GetSection("EmailSettings").Exists();

            var storageConfigured =
                _configuration.GetSection("DocumentStorage").Exists();

            var maxUpload =
                _configuration["DocumentStorage:MaxUploadSizeMB"];

            var allowedTypes =
                _configuration["DocumentStorage:AllowedExtensions"];

            return new SettingsViewModel
            {
                EnvironmentName = _environment.EnvironmentName,

                DatabaseServer = databaseServer,
                DatabaseName = databaseName,

                EmailConfiguration = emailConfigured
                    ? "Configuration section present"
                    : "Not configured",

                DocumentStorage = storageConfigured
                    ? "Configuration section present"
                    : "Not configured",

                MaximumUploadSize =
                    !string.IsNullOrWhiteSpace(maxUpload)
                        ? maxUpload + " MB (configured value)"
                        : "Not configured",

                AllowedFileTypes =
                    !string.IsNullOrWhiteSpace(allowedTypes)
                        ? allowedTypes
                        : "Not configured"
            };
        }
    }
}
