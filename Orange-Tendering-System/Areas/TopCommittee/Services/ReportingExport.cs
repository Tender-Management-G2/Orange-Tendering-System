using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

public static class ReportingExport
{
    public static Task<byte[]> Pdf(Controller controller, IRazorViewEngine engine, ReportingPage page,
        string contentRoot, CancellationToken cancellationToken)
        => PdfView(controller, engine, page, "/Areas/TopCommittee/Views/Reports/Pdf.cshtml", contentRoot, cancellationToken);

    public static async Task<byte[]> PdfView(Controller controller, IRazorViewEngine engine, object model,
        string viewPath, string contentRoot, CancellationToken cancellationToken)
    {
        var result = engine.GetView(null, viewPath, true);
        if (!result.Success) throw new InvalidOperationException("The report export template is unavailable.");
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        await result.View.RenderAsync(new ViewContext(controller.ControllerContext, result.View, data,
            controller.TempData, writer, new HtmlHelperOptions()));
        return await BrowserReportPdf.Create(writer.ToString(), contentRoot, cancellationToken);
    }
}
