// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Saja
// Module: Supplier Portal
// Purpose: MVC endpoints for the Profile pages of the Supplier workspace.
// Status: Company profile with contact editing, filtered by the authenticated account.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Areas.Supplier.Controllers
{ //بقرا  بيانات من الداتابيس المرتبطة بالاي دي الي دخل فيه المستخدم وبشوف شو الشركة الي مرتبطة فيه
    [Area("Supplier")]
    [Authorize(Roles = SystemRoles.Supplier)]
    public class ProfileController(SupplierProfileService profileService) : Controller
    {
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return Challenge();

            var model = await profileService.GetProfileAsync(userId);
            if (model.AccessMessage is not null)
                Response.StatusCode = StatusCodes.Status403Forbidden;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateContact(
            [Bind(Prefix = "Contact")] SupplierContactInputModel input)
        {
            // 1. نأخذ رقم المستخدم المسجل دخوله.
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Challenge();
            }

            // 2. إذا البيانات غير صحيحة، نعرض الصفحة مع الأخطاء.
            if (!ModelState.IsValid)
            {
                var model = await profileService.GetProfileAsync(userId);
                if (model.AccessMessage is not null)
                    Response.StatusCode = StatusCodes.Status403Forbidden;
                return View("Index", model);
            }

            // 3. الخدمة تتحقق من الشركة وتعدل البيانات وتحفظها.
            bool saved = await profileService.UpdateContactAsync(userId, input);
            if (saved == false)
            {
                return Forbid();
            }

            // 4. بعد نجاح الحفظ نرجع للبروفايل مع رسالة نجاح.
            TempData["ProfileSuccess"] = "Contact details saved successfully.";
            return RedirectToAction("Index");
        }
    }
}
