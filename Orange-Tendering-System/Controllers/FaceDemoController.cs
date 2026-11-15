// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Ahmad
// Module: Authentication & Biometrics
// Purpose: DEVELOPMENT-ONLY face recognition test bench at /face-demo. Register a test face, verify it as often as
//   you like, reset it. Uses the real FaceService one-time token, sample checks and matching
//   (FaceService.Match + Biometric:FaceMatchThreshold) and the same biometric.js/face-api.js capture as sign-in.
// Not authentication: no Identity user, no Supplier, no FaceTemplates row and no sign-in cookie is ever created.
//   The test face lives in server memory for this browser (random cookie id) and is lost on restart.
//   Every action returns 404 outside the Development environment.
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Services.Biometric;

namespace Orange_Tendering_System.Controllers
{
    [AllowAnonymous, Route("face-demo")]
    public class FaceDemoController(
        FaceService faces,
        IMemoryCache cache,
        IOptions<BiometricOptions> options,
        IWebHostEnvironment environment) : Controller
    {
        private const string CookieName = "OrangeFaceDemo";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (!environment.IsDevelopment())
                context.Result = NotFound();
        }

        // GET /face-demo
        [HttpGet("")]
        public IActionResult Index()
        {
            var id = DemoId();
            if (id is null)
            {
                id = Base64Url.Encode(RandomNumberGenerator.GetBytes(16));
                Response.Cookies.Append(CookieName, id, new CookieOptions
                {
                    HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, Path = "/face-demo"
                });
            }

            return View(new FaceDemoViewModel
            {
                TestFaceSamples = Templates(id)?.Count ?? 0,
                Threshold = options.Value.FaceMatchThreshold
            });
        }

        // POST /face-demo/challenge — a one-time token for one attempt, as in the real flow.
        [HttpPost("challenge"), ValidateAntiForgeryToken]
        public IActionResult StartChallenge() =>
            DemoId() is { } id ? Json(faces.CreateChallenge(FaceService.DemoPurpose, id)) : NoDemo();

        [HttpPost("register"), ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
        public IActionResult Register([FromBody] FaceSubmission submission)
        {
            if (DemoId() is not { } id) return NoDemo();
            var (problem, templates) = faces.CheckDemoEnrollment(submission, id);
            if (problem is not null)
                return Json(new { registered = false, problem });

            cache.Set(Key(id), templates, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2) });
            return Json(new { registered = true, samples = templates.Count });
        }

        [HttpPost("verify"), ValidateAntiForgeryToken, RequestSizeLimit(400_000)]
        public IActionResult Verify([FromBody] FaceSubmission submission)
        {
            if (DemoId() is not { } id) return NoDemo();
            var templates = Templates(id);
            if (templates is null || templates.Count == 0)
                return Json(new { stage = "noTestFace" });

            var (problem, match) = faces.CheckDemoVerification(submission, id, templates);
            if (problem is not null || match is null)
                return Json(new { stage = "rejected", problem });

            return Json(new
            {
                stage = "match", matched = match.Matched,
                bestDistances = match.BestDistances.Select(d => Math.Round(d, 3)),
                averageDistance = Math.Round(match.AverageDistance, 3),
                matchingSamples = match.MatchingSamples, requiredSamples = match.RequiredSamples,
                threshold = match.Threshold
            });
        }

        [HttpPost("reset"), ValidateAntiForgeryToken]
        public IActionResult Reset()
        {
            if (DemoId() is { } id) cache.Remove(Key(id));
            return Json(new { reset = true });
        }

        private string? DemoId() =>
            Request.Cookies.TryGetValue(CookieName, out var id) && id.Length is > 10 and < 64 ? id : null;

        private List<float[]>? Templates(string id) => cache.Get<List<float[]>>(Key(id));

        private static string Key(string id) => "face-demo:" + id;

        private BadRequestObjectResult NoDemo() => BadRequest(new { error = "Reload /face-demo to start a demo session." });
    }
}
