// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Tariq — Public Site (Contact page has no dedicated Trello card)
// Module: Public Site
// Purpose: Endpoint for the public contact form (Views/Shared/_ContactForm.cshtml, wwwroot/js/contact.js).
// Status: NOT IMPLEMENTED. Answers 501 so the page honestly reports that nothing was sent or saved.
//         There is no contact table in the approved schema; decide with the team before storing inquiries.
using Microsoft.AspNetCore.Mvc;

namespace Orange_Tendering_System.Controllers
{
    [Route("contact")]
    public class ContactController : Controller
    {
        [HttpPost("submit")]
        [ValidateAntiForgeryToken]
        public IActionResult Submit()
        {
            return StatusCode(StatusCodes.Status501NotImplemented,
                new { message = "Contact form is not connected yet. Nothing was sent or saved." });
        }
    }
}
