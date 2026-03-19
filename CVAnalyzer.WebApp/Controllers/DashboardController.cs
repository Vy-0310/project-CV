using CVAnalyzer.WebApp.Data;
using CVAnalyzer.WebApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization; // <-- Thêm using này
using System.Linq;
using System.Threading.Tasks;

namespace CVAnalyzer.WebApp.Controllers
{
    [Authorize] // Chỉ người đã đăng nhập mới xem được
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        // [HttpGet]
        public async Task<IActionResult> Index()
        {
            // ==========================================================
            // === LOGIC ĐÃ ĐƯỢC VIẾT LẠI ĐỂ ĐỌC TỪ BẢNG MỚI ===
            // ==========================================================

            // 1. Tìm timestamp (thời gian) crawl gần nhất TỪ BẢNG JOBPOSTS
            var latestTimestamp = await _context.JobPosts
                                                .MaxAsync(jp => (DateTime?)jp.CrawledAt);

            if (latestTimestamp == null)
            {
                // Chưa có dữ liệu, trả về View rỗng
                return View(new DashboardViewModel());
            }

            var culture = new CultureInfo("vi-VN"); // Dùng để viết hoa chữ cái đầu

            // 2. Lấy dữ liệu cho từng biểu đồ (Query riêng biệt, hiệu suất cao)

            // BIỂU ĐỒ 1: Top 10 Kỹ năng IT tại TP.HCM
            var topItHcmData = await _context.JobPostSkills
                .AsNoTracking()
                .Where(jps => jps.JobPost.JobCategory == "IT" &&
                              jps.JobPost.Location == "TP.HCM" &&
                              jps.JobPost.CrawledAt == latestTimestamp.Value)
                .GroupBy(jps => jps.SkillName) // Nhóm theo tên kỹ năng
                .Select(g => new { SkillName = g.Key, Frequency = g.Count() }) // Đếm số lần xuất hiện
                .OrderByDescending(s => s.Frequency)
                .Take(10)
                .ToListAsync();

            // BIỂU ĐỒ 2: Top 10 Kỹ năng Marketing tại Hà Nội
            var topMarketingHnData = await _context.JobPostSkills
                .AsNoTracking()
                .Where(jps => jps.JobPost.JobCategory == "Marketing" &&
                              jps.JobPost.Location == "Hà Nội" &&
                              jps.JobPost.CrawledAt == latestTimestamp.Value)
                .GroupBy(jps => jps.SkillName)
                .Select(g => new { SkillName = g.Key, Frequency = g.Count() })
                .OrderByDescending(s => s.Frequency)
                .Take(10)
                .ToListAsync();

            // BIỂU ĐỒ 3: So sánh 3 ngành (Tổng số kỹ năng đã crawl)
            var categoryComparisonData = await _context.JobPostSkills
                .AsNoTracking()
                .Where(jps => jps.JobPost.CrawledAt == latestTimestamp.Value)
                .GroupBy(jps => jps.JobPost.JobCategory) // Nhóm theo Category của JobPost
                .Select(g => new
                {
                    Category = g.Key,
                    TotalFrequency = g.Count() // Đếm tổng số kỹ năng tìm thấy
                })
                .ToListAsync();


            // 3. Xử lý và phân loại dữ liệu (Giữ nguyên logic Aggregate của bạn)
            var viewModel = new DashboardViewModel
            {
                // BIỂU ĐỒ 1
                TopSkillsItHcm = topItHcmData
                    .Aggregate(new ChartData(), (chart, skill) =>
                    {
                        chart.Labels.Add(culture.TextInfo.ToTitleCase(skill.SkillName));
                        chart.Data.Add(skill.Frequency);
                        return chart;
                    }),

                // BIỂU ĐỒ 2
                TopSkillsMarketingHn = topMarketingHnData
                    .Aggregate(new ChartData(), (chart, skill) =>
                    {
                        chart.Labels.Add(culture.TextInfo.ToTitleCase(skill.SkillName));
                        chart.Data.Add(skill.Frequency);
                        return chart;
                    }),

                // BIỂU ĐỒ 3
                CategoryComparison = categoryComparisonData
                    .Aggregate(new ChartData(), (chart, item) =>
                    {
                        chart.Labels.Add(item.Category);
                        chart.Data.Add(item.TotalFrequency);
                        return chart;
                    })
            };

            return View(viewModel);
        }
    }
}