using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers
{
    // JSON endpoints of the floating AI Chat Assistant. Every action works only on the signed-in user's own conversations.
    [Authorize, AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AiChatController(AiChatService chat) : Controller
    {
        public sealed record SendRequest(int? ConversationId, string? Message, string? Page = null);

        [HttpGet] public IActionResult Help() => Json(OrangeHelpKnowledge.Overview(User));
        [HttpGet] public async Task<IActionResult> Conversations() => Json(new { items = await chat.ListAsync(User) });

        [HttpGet]
        public async Task<IActionResult> Messages(int id)
        {
            var items = await chat.MessagesAsync(User, id);
            return items == null ? NotFound(new { error = "Conversation not found." }) : Json(new { items });
        }

        [HttpPost, RequestSizeLimit(20_000)]
        public async Task<IActionResult> Send([FromBody] SendRequest? request, CancellationToken ct)
        {
            if (!ModelState.IsValid || request == null) return BadRequest(new { error = "Enter a valid question." });
            try { return Json(await chat.SendAsync(User, request.ConversationId, request.Message, ct, request.Page)); }
            catch (ChatException e) { return StatusCode(e.Status, new { error = e.Message, conversationId = e.ConversationId }); }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            try { return await chat.DeleteAsync(User, id) ? Json(new { ok = true }) : NotFound(new { error = "Conversation not found." }); }
            catch (ChatException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }
    }
}
