using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Assistant;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Controllers
{
    // JSON endpoints of Orange AI (the floating chat). Every action works only on the signed-in user's own conversations and files;
    // data access, exports and downloads are authorized on the server (AssistantAccess / AssistantDataTools / AiChatFileStore).
    [Authorize, AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AiChatController(AiChatService chat, AiChatFileStore files, AiKnowledgeService knowledge) : Controller
    {
        public sealed record SendRequest(int? ConversationId, string? Message, string? Page = null, Guid? ActionId = null);
        public sealed record SaveKnowledgeRequest(Guid PreviewId, string? Scope, string? Title, int? ReplaceId = null);
        public sealed record ReviewKnowledgeRequest(int Id, bool Approve);
        private bool UiArabic => UiLanguage.IsArabic(HttpContext);

        [HttpGet] public IActionResult Help() => Json(OrangeHelpKnowledge.Overview(User));
        [HttpGet] public async Task<IActionResult> Conversations(string? page = null) => Json(new { items = await chat.ListAsync(User, page) });

        // Welcome card data. loginId changes only with a new sign-in (see Program.cs OnSigningIn), so the browser greets once per login.
        [HttpGet]
        public async Task<IActionResult> Welcome(CancellationToken ct, string? page = null)
        {
            var auth = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            var loginId = auth.Properties?.Items.TryGetValue(OrangeLogin.ItemKey, out var value) == true ? value : null;
            try { return Json(new { loginId, welcome = await chat.WelcomeAsync(User, UiArabic, ct, page) }); }
            catch (ChatException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> Messages(int id, string? page = null)
        {
            var items = await chat.MessagesAsync(User, id, page);
            return items == null ? NotFound(new { error = "Conversation not found." }) : Json(new { items });
        }

        [HttpPost, RequestSizeLimit(20_000)]
        public async Task<IActionResult> Send([FromBody] SendRequest? request, CancellationToken ct)
        {
            if (!ModelState.IsValid || request == null) return BadRequest(new { error = "Enter a valid question." });
            try { return Json(await chat.SendAsync(User, request.ConversationId, request.Message, ct, request.Page, UiArabic, request.ActionId)); }
            catch (ChatException e) { return StatusCode(e.Status, new { error = e.Message, conversationId = e.ConversationId }); }
        }

        [HttpPost, RequestSizeLimit(AiChatFileStore.MaxUploadBytes + 64 * 1024), RequestFormLimits(MultipartBodyLengthLimit = AiChatFileStore.MaxUploadBytes + 64 * 1024)]
        public async Task<IActionResult> Upload([FromForm] int? conversationId, IFormFile? file, CancellationToken ct, [FromForm] string? page = null)
        {
            if (file == null) return BadRequest(new { error = "Choose a file to upload." });
            try { return Json(await chat.UploadAsync(User, conversationId, file, UiArabic, ct, page)); }
            catch (ChatException e) { return StatusCode(e.Status, new { error = e.Message, conversationId = e.ConversationId }); }
        }

        // Authorized download: the owner only, before expiry. The stored name is random; the browser receives the display name.
        [HttpGet("/AiChat/Download/{id:guid}")]
        public async Task<IActionResult> Download(Guid id, CancellationToken ct)
        {
            var userId = AssistantAccess.UserId(User);
            if (userId == null) return Challenge();
            try
            {
                var (file, path) = await files.OpenForDownloadAsync(userId, id, ct);
                Response.Headers["X-Content-Type-Options"] = "nosniff";
                return PhysicalFile(path, file.ContentType, file.FileName);
            }
            catch (AiFileException e) { return StatusCode(e.Status, e.Message); }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, string? page = null)
        {
            try { return await chat.DeleteAsync(User, id, page) ? Json(new { ok = true }) : NotFound(new { error = "Conversation not found." }); }
            catch (ChatException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }

        [HttpGet]
        public async Task<IActionResult> Knowledge(CancellationToken ct)
        {
            try
            {
                var userId = AssistantAccess.UserId(User);
                if (userId == null) return Challenge();
                var items = await knowledge.ListAsync(userId, ct);
                var pending = User.IsInRole(Orange_Tendering_System.Models.SystemRoles.Admin)
                    ? await knowledge.PendingSharedAsync(userId, ct) : [];
                return Json(new { items, pending });
            }
            catch (AiKnowledgeException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }

        [HttpPost, RequestSizeLimit(AiChatFileStore.MaxUploadBytes + 64 * 1024), RequestFormLimits(MultipartBodyLengthLimit = AiChatFileStore.MaxUploadBytes + 64 * 1024)]
        public async Task<IActionResult> KnowledgePreview(IFormFile? file, CancellationToken ct)
        {
            if (file == null) return BadRequest(new { error = "Choose a file to preview." });
            var userId = AssistantAccess.UserId(User);
            if (userId == null) return Challenge();
            try { return Json(await knowledge.PreviewAsync(userId, file, UiArabic, ct)); }
            catch (AiFileException e) { return StatusCode(e.Status, new { error = e.Message }); }
            catch (AiKnowledgeException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> KnowledgeSave([FromBody] SaveKnowledgeRequest? request, CancellationToken ct)
        {
            if (request == null || request.PreviewId == Guid.Empty) return BadRequest(new { error = "Review the file preview before saving." });
            var userId = AssistantAccess.UserId(User);
            if (userId == null) return Challenge();
            try { return Json(new { item = await knowledge.SavePreviewAsync(userId, request.PreviewId, request.Scope ?? "", request.Title, request.ReplaceId, ct) }); }
            catch (AiKnowledgeException e) { return StatusCode(e.Status, new { error = e.Message }); }
            catch (HttpRequestException) { return StatusCode(503, new { error = "The embedding model is unavailable. The knowledge item was not saved; check the configured Ollama embedding model." }); }
        }

        [HttpGet]
        public async Task<IActionResult> KnowledgeSearch(string? q, CancellationToken ct)
        {
            var userId = AssistantAccess.UserId(User);
            if (userId == null) return Challenge();
            try { return Json(await knowledge.SearchAsync(userId, q ?? "", 8, ct)); }
            catch (AiKnowledgeException e) { return StatusCode(e.Status, new { error = e.Message }); }
            catch (HttpRequestException) { return StatusCode(503, new { error = "The search provider is unavailable." }); }
        }

        [HttpPost]
        public async Task<IActionResult> KnowledgeReview([FromBody] ReviewKnowledgeRequest? request, CancellationToken ct)
        {
            if (request == null || request.Id <= 0) return BadRequest(new { error = "Choose a knowledge item." });
            var userId = AssistantAccess.UserId(User);
            if (userId == null) return Challenge();
            try { await knowledge.ReviewAsync(userId, request.Id, request.Approve, ct); return Json(new { ok = true }); }
            catch (AiKnowledgeException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> KnowledgeDelete(int id, CancellationToken ct)
        {
            var userId = AssistantAccess.UserId(User);
            if (userId == null) return Challenge();
            try { await knowledge.DeleteAsync(userId, id, ct); return Json(new { ok = true }); }
            catch (AiKnowledgeException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> KnowledgeReindex(int id, CancellationToken ct)
        {
            var userId = AssistantAccess.UserId(User);
            if (userId == null) return Challenge();
            try { await knowledge.ReindexAsync(userId, id, ct); return Json(new { ok = true }); }
            catch (AiKnowledgeException e) { return StatusCode(e.Status, new { error = e.Message }); }
            catch (HttpRequestException) { return StatusCode(503, new { error = "The embedding model is unavailable." }); }
        }
    }
}
