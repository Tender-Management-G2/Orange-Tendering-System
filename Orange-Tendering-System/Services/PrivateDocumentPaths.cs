namespace Orange_Tendering_System.Services;

public static class PrivateDocumentPaths
{
    public static string Resolve(string contentRoot, string storedPath)
    {
        TenderWorkflowService.Require(!string.IsNullOrWhiteSpace(storedPath), "Document storage path is missing.");
        var privateRoot = Path.GetFullPath(Path.Combine(contentRoot, "App_Data"));
        var relative = storedPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.IsPathRooted(relative) ? relative :
            relative.StartsWith("App_Data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(contentRoot, relative) : Path.Combine(privateRoot, "TenderDocuments", relative));
        var allowed = new[] { "TenderDocuments", "SupplierDocuments", "SupplierUploads" }
            .Any(name => path.StartsWith(Path.Combine(privateRoot, name) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        TenderWorkflowService.Require(allowed, "Invalid private document storage path.");
        return path;
    }
}
