// ORANGE TENDER MANAGEMENT SYSTEM — Owner: Ahmad — Module: Authentication & Biometrics
// DEVELOPMENT-ONLY face recognition test bench at /face-demo: register a test face, verify it as often as you like, reset it.
// It uses the real FaceService token, sample checks and matching (FaceService.Match + Biometric:FaceMatchThreshold) and the
// same capture code as sign-in (wwwroot/js/biometric.js).
// It is NOT authentication: no user, no Supplier, no FaceTemplates row and no sign-in cookie is ever created. The test face
// lives in server memory for this browser (a random cookie id) and is lost on restart. Outside Development every action is 404.
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Controllers;

[AllowAnonymous, Route("face-demo")]
public class FaceDemoController : Controller
{
    private const string CookieName = "OrangeFaceDemo";

    private readonly FaceService faces;
    private readonly IMemoryCache cache;
    private readonly BiometricOptions options;
    private readonly IWebHostEnvironment environment;

    public FaceDemoController(FaceService faces, IMemoryCache cache, IOptions<BiometricOptions> options, IWebHostEnvironment environment)
    {
        this.faces = faces;
        this.cache = cache;
        this.options = options.Value;
        this.environment = environment;
    }

    // Runs before every action: the demo exists only in Development.
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!environment.IsDevelopment())
        {
            context.Result = NotFound();
        }
    }

    // GET /face-demo
    [HttpGet("")]
    public IActionResult Index()
    {
        string? id = DemoId();
        if (id == null)
        {
            id = Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
            CookieOptions cookie = new CookieOptions();
            cookie.HttpOnly = true;
            cookie.Secure = true;
            cookie.SameSite = SameSiteMode.Strict;
            cookie.IsEssential = true;
            cookie.Path = "/face-demo";
            Response.Cookies.Append(CookieName, id, cookie);
        }

        FaceDemoViewModel model = new FaceDemoViewModel();
        List<float[]>? templates = Templates(id);
        if (templates != null)
        {
            model.TestFaceSamples = templates.Count;
        }
        model.Threshold = options.FaceMatchThreshold;
        return View(model);
    }

    // POST /face-demo/challenge — a one-time token for one attempt, as in the real flow.
    [HttpPost("challenge"), ValidateAntiForgeryToken]
    public IActionResult StartChallenge()
    {
        string? id = DemoId();
        if (id == null)
        {
            return NoDemo();
        }
        return Json(faces.CreateChallenge(FaceService.DemoPurpose, id));
    }

    [HttpPost("register"), ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
    public IActionResult Register([FromBody] FaceSubmission submission)
    {
        string? id = DemoId();
        if (id == null)
        {
            return NoDemo();
        }

        string? problem = faces.CheckDemoSubmission(submission, id);
        if (problem != null)
        {
            return Json(new { registered = false, problem = problem });
        }

        List<float[]> templates = faces.DemoTemplates(submission.Descriptors);
        cache.Set(CacheKey(id), templates, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
        return Json(new { registered = true, samples = templates.Count });
    }

    [HttpPost("verify"), ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
    public IActionResult Verify([FromBody] FaceSubmission submission)
    {
        string? id = DemoId();
        if (id == null)
        {
            return NoDemo();
        }

        List<float[]>? templates = Templates(id);
        if (templates == null || templates.Count == 0)
        {
            return Json(new { stage = "noTestFace" });
        }

        string? problem = faces.CheckDemoSubmission(submission, id);
        if (problem != null)
        {
            return Json(new { stage = "rejected", problem = problem });
        }

        FaceMatch match = faces.Match(submission.Descriptors, templates);
        List<double> rounded = new List<double>();
        foreach (double distance in match.BestDistances)
        {
            rounded.Add(Math.Round(distance, 3));
        }
        return Json(new
        {
            stage = "match",
            matched = match.Matched,
            bestDistances = rounded,
            averageDistance = Math.Round(match.AverageDistance, 3),
            matchingSamples = match.MatchingSamples,
            requiredSamples = match.RequiredSamples,
            threshold = match.Threshold
        });
    }

    [HttpPost("reset"), ValidateAntiForgeryToken]
    public IActionResult Reset()
    {
        string? id = DemoId();
        if (id != null)
        {
            cache.Remove(CacheKey(id));
        }
        return Json(new { reset = true });
    }

    // The random id of this demo browser, from its cookie (null when missing or malformed).
    private string? DemoId()
    {
        string? id = Request.Cookies[CookieName];
        if (id == null || id.Length <= 10 || id.Length >= 64)
        {
            return null;
        }
        return id;
    }

    private List<float[]>? Templates(string id)
    {
        return cache.Get<List<float[]>>(CacheKey(id));
    }

    private static string CacheKey(string id)
    {
        return "face-demo:" + id;
    }

    private BadRequestObjectResult NoDemo()
    {
        return BadRequest(new { error = "Reload /face-demo to start a demo session." });
    }
}
