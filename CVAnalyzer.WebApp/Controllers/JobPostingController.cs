using CVAnalyzer.WebApp.Data;
using CVAnalyzer.WebApp.Models;
using CVAnalyzer.WebApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json; // Cần cho việc parse điểm số

namespace CVAnalyzer.WebApp.Controllers
{
    [Authorize] // Bắt buộc đăng nhập
    public class JobPostingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public JobPostingController(ApplicationDbContext context)
        {
            _context = context;
        }

        // [Get] /JobPosting
        // Trang Dashboard chính của HR, liệt kê tất cả Job Posting
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));

            var jobPostings = await _context.JobPostings
                .Where(jp => jp.UserId == userId)
                .Include(jp => jp.Cvs) // Lấy cả các CV liên quan
                .OrderByDescending(jp => jp.CreatedAt)
                .ToListAsync();

            // (Bạn sẽ tạo View cho trang này ở Giai đoạn 3)
            return View(jobPostings);
        }

        // [Get] /JobPosting/Create
        // Hiển thị form để tạo Job Posting mới
        public IActionResult Create()
        {
            return View(new JobPostingCreateViewModel());
        }

        // [Post] /JobPosting/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(JobPostingCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue("UserId"));

            var jobPosting = new JobPosting
            {
                Title = model.Title,
                JobDescriptionText = model.JobDescriptionText,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.JobPostings.Add(jobPosting);
            await _context.SaveChangesAsync();

            // Sau khi tạo xong, chuyển đến trang Dashboard của Job đó
            return RedirectToAction("Dashboard", new { id = jobPosting.Id });
        }

        
        // Trang quan trọng nhất: Hiển thị chi tiết 1 Job Posting
        // và danh sách CV đã được upload cho job đó
        public async Task<IActionResult> Dashboard(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));

            var jobPosting = await _context.JobPostings
                .Include(jp => jp.Cvs) // Lấy danh sách CV
                .FirstOrDefaultAsync(jp => jp.Id == id && jp.UserId == userId);

            if (jobPosting == null)
            {
                return NotFound();
            }
            return View(jobPosting);
        }
    }
}