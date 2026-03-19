using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CVAnalyzer.WebApp.Data;
using CVAnalyzer.WebApp.ViewModels;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace CVAnalyzer.WebApp.Controllers
{
    [Authorize] // Chỉ người dùng đã đăng nhập mới vào được đây
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // [GET] /Account/ChangePassword
        // Hiển thị form đổi mật khẩu
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        // [POST] /Account/ChangePassword
        // Xử lý việc đổi mật khẩu
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue("UserId"));
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
            {
                return NotFound();
            }

            // 1. Kiểm tra xem mật khẩu cũ có đúng không
            if (!BCrypt.Net.BCrypt.Verify(model.OldPassword, user.PasswordHash))
            {
                ModelState.AddModelError("OldPassword", "Mật khẩu cũ không chính xác.");
                return View(model);
            }

            // 2. Nếu đúng, mã hóa và cập nhật mật khẩu mới
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            _context.Update(user);
            await _context.SaveChangesAsync();

            // Thêm một thông báo thành công để hiển thị
            TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
            return RedirectToAction("ChangePassword");
        }
    }
}