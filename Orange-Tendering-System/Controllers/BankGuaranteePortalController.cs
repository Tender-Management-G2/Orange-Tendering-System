using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers;

[AllowAnonymous, Route("bank-verification")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BankGuaranteePortalController(BankGuaranteeVerification verification) : Controller
{
    [HttpGet("demo")]
    public IActionResult Demo() => Show(verification.Demo());

    [HttpGet("{token}", Name = BankGuaranteeVerification.RouteName)]
    public IActionResult Verify(string token) => Show(verification.CheckLink(token));

    private IActionResult Show(BankGuaranteeVerification.Result result)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (result.State == BankGuaranteeVerification.Status.LinkUnavailable)
            Response.StatusCode = StatusCodes.Status404NotFound;
        return View("Index", result);
    }
}
