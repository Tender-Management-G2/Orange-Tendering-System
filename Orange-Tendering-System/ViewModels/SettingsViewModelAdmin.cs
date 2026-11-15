

// sara rjoub 


namespace Orange_Tendering_System.ViewModels
{
    public class SettingsViewModel
    {
        public string EnvironmentName { get; set; } = "";

        public string DatabaseServer { get; set; } = "";

        public string DatabaseName { get; set; } = "";

        public string EmailConfiguration { get; set; } = "";

        public string DocumentStorage { get; set; } = "";

        public string MaximumUploadSize { get; set; } = "";

        public string AllowedFileTypes { get; set; } = "";
    }
}
