using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers
{
    // JSON endpoints of the floating AI Chat Assistant. Every action works only on the signed-in user's own conversations.
    [Authorize, AutoValidateAntiforgeryToken]
    public sealed class AiChatController(AiChatService chat) : Controller
    {
        public sealed record SendRequest(int? ConversationId, string? Message);

        [HttpGet] public async Task<IActionResult> Conversations() => Json(new { items = await chat.ListAsync(User) });

        [HttpGet]
        public async Task<IActionResult> Messages(int id)
        {
            var items = await chat.MessagesAsync(User, id);
            return items == null ? NotFound(new { error = "Conversation not found." }) : Json(new { items });
        }

        [HttpPost, RequestSizeLimit(20_000)]
        public async Task<IActionResult> Send([FromBody] SendRequest request, CancellationToken ct)
        {
            try { return Json(await chat.SendAsync(User, request.ConversationId, request.Message, ct)); }
            catch (ChatException e) { return StatusCode(e.Status, new { error = e.Message }); }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id) => await chat.DeleteAsync(User, id) ? Json(new { ok = true }) : NotFound(new { error = "Conversation not found." });
    }
}
