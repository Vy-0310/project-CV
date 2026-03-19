using CVAnalyzer.Data;
using CVAnalyzer.WebApp.Data;
using CVAnalyzer.WebApp.Models;
using CVAnalyzer.WebApp.Services;
using CVAnalyzer.WebApp.ViewModels;
using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using System.IO;
using Quartz;
using CVAnalyzer.Data.Models;

namespace CVAnalyzer.WebApp.Controllers
{
    [Authorize]
    public class CvController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly OpenAIClient _openAIClient;
        private readonly IConverter _converter;
        private readonly IViewRendererService _viewRendererService;
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly ILogger<CvController> _logger;

        public CvController(
            ApplicationDbContext context,
            IWebHostEnvironment webHostEnvironment,
            OpenAIClient openAIClient,
            IConverter converter,
            IViewRendererService viewRendererService,
            ISchedulerFactory schedulerFactory,
            ILogger<CvController> logger
            )
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _openAIClient = openAIClient;
            _converter = converter;
            _viewRendererService = viewRendererService;
            _schedulerFactory = schedulerFactory;
            _logger = logger;
        }

        

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var userCvs = await _context.Cvs
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UploadedAt)
                .ToListAsync();
            return View(userCvs);
        }

        [HttpGet]
        public IActionResult Upload()
        {
            return View(new CvUploadViewModel());
        }

        [HttpGet]
        public async Task<IActionResult> Result(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cv == null) return NotFound();

            return View(cv);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadAnalysisReport(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cv == null) return NotFound();
            string htmlContent = await _viewRendererService.RenderToStringAsync("Cv/_AnalysisReportPdf", cv);

            var globalSettings = new GlobalSettings { ColorMode = ColorMode.Color, Orientation = Orientation.Portrait, PaperSize = PaperKind.A4 };
            var objectSettings = new ObjectSettings { PagesCount = true, HtmlContent = htmlContent, WebSettings = { DefaultEncoding = "utf-8" } };
            var pdf = new HtmlToPdfDocument() { GlobalSettings = globalSettings, Objects = { objectSettings } };
            byte[] pdfBytes = _converter.Convert(pdf);
            string fileName = $"BaoCaoPhanTich_{Path.GetFileNameWithoutExtension(cv.FileName)}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RewriteSuggestion([FromBody] RewriteRequestModel model)
        {
            if (string.IsNullOrWhiteSpace(model.SuggestionText))
            {
                return BadRequest(new { error = "Nội dung gốc không được rỗng." });
            }
            // Logic viết lại câu văn yếu
            var rewritePrompt = $@"Bạn là một chuyên gia viết CV. Hãy viết lại câu sau cho chuyên nghiệp hơn, sử dụng mô hình STAR nếu có thể: ""{model.SuggestionText}""";
            var rewrittenText = await CallOpenAI(rewritePrompt);
            rewrittenText = rewrittenText.Trim('\"');
            return Ok(new { newText = rewrittenText });
        }




        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(CvUploadViewModel model)
        {
            // 1. Nếu không có file nào được chọn
            if (model.CvFiles == null || !model.CvFiles.Any())
            {
                ModelState.AddModelError("CvFiles", "Vui lòng chọn file để tải lên.");
                return View(model);
            }

            var userIdString = User.FindFirstValue("UserId");
            if (!int.TryParse(userIdString, out var userId)) return Unauthorized();

            // Danh sách lưu các ID đã xử lý để điều hướng
            var processedIds = new List<int>();

            // 2. [QUAN TRỌNG] Vòng lặp xử lý từng file một
            foreach (var file in model.CvFiles)
            {
                // Kiểm tra dung lượng (5MB)
                if (file.Length > 5 * 1024 * 1024) continue;

                string uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                Directory.CreateDirectory(uploadsFolder);

                // Lưu file vật lý
                await using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                // Trích xuất Text
                string? cvText = ExtractTextFromPdfWithPdfPig(filePath);
                if (string.IsNullOrWhiteSpace(cvText)) continue;

                // Làm sạch Text
                cvText = System.Text.RegularExpressions.Regex.Replace(cvText, @"[^\p{L}\p{N}\s.,;!?()\-@]", "", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));

                // Phân tích AI
                (string jobCategory, string location) = await ClassifyJobAndLocationAsync(cvText);
                string rawAnalysisResponse = await AnalyzeCvAsync(cvText, model.JobDescription, jobCategory);

                var jsonMatch = System.Text.RegularExpressions.Regex.Match(rawAnalysisResponse, @"\{[\s\S]*\}");
                string analysisJson = jsonMatch.Success ? jsonMatch.Value : "{}";

                // Lưu vào DB
                var cv = new Cv
                {
                    UserId = userId,
                    FileName = file.FileName,       // Tên file gốc
                    StoredFileName = uniqueFileName, // Tên file trên server
                    AnalysisResult = analysisJson,
                    ExtractedText = cvText,
                    UploadedAt = DateTime.UtcNow,
                    JobDescriptionText = model.JobDescription,
                    JobCategory = jobCategory,
                    Location = location
                };

                _context.Cvs.Add(cv);
                await _context.SaveChangesAsync(); // Lưu từng cái để lấy ID ngay

                processedIds.Add(cv.Id);
            }

            
            if (processedIds.Count == 1)
            {
                return RedirectToAction("Result", new { id = processedIds.First() });
            }

            
            return RedirectToAction("History");
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GetMarketAnalysis(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (cv == null || string.IsNullOrEmpty(cv.AnalysisResult))
            {
                return NotFound(new { message = "Không tìm thấy dữ liệu CV để phân tích." });
            }

            try
            {
                // 1. XÁC ĐỊNH NGÀNH VÀ ĐỊA ĐIỂM
                string jobCategory = cv.JobCategory ?? "IT";
                string location = cv.Location ?? "TP.HCM";

               

                // Kiểm tra xem có job nào trong 3 ngày qua không
                var hasRecentData = await _context.JobPosts
                    .AnyAsync(jp => jp.JobCategory == jobCategory &&
                                    jp.Location == location &&
                                    jp.CrawledAt > DateTime.UtcNow.AddDays(-3));

                // Nếu không có dữ liệu mới trong 3 ngày -> Kích hoạt Crawl
                if (!hasRecentData)
                {
                    _logger.LogInformation("Dữ liệu quá cũ hoặc chưa có cho '{Category}' @ '{Location}'. Kích hoạt Crawl.", jobCategory, location);
                    await TriggerOnDemandCrawl(jobCategory, location);
                    return Ok(new
                    {
                        status = "crawling",
                        message = $"Hệ thống đang cập nhật dữ liệu mới nhất cho ngành '{jobCategory}' tại '{location}'. Vui lòng thử lại sau 1-2 phút."
                    });
                }

                // 3. LẤY DANH SÁCH ID CỦA 50 JOB MỚI NHẤT (Thay vì so sánh millisecond)
                var latestJobPostIds = await _context.JobPosts
                    .AsNoTracking()
                    .Where(jp => jp.JobCategory == jobCategory && jp.Location == location)
                    .OrderByDescending(jp => jp.CrawledAt)
                    .Take(50) 
                    .Select(jp => jp.Id)
                    .ToListAsync();

                // 4. THỐNG KÊ TOP KỸ NĂNG (Dựa trên 50 Job này)
                var topSkillsData = await _context.JobPostSkills
                    .AsNoTracking()
                    .Where(jps => latestJobPostIds.Contains(jps.JobPostId))
                    .GroupBy(jps => jps.SkillName)
                    .Select(g => new
                    {
                        SkillName = g.Key,
                        Frequency = g.Count()
                    })
                    .OrderByDescending(s => s.Frequency)
                    .Take(10)
                    .ToListAsync();

                var relevantJobs = await _context.JobPosts
                    .AsNoTracking()
                    .Where(jp => latestJobPostIds.Contains(jp.Id))
                    .OrderByDescending(jp => jp.CrawledAt)
                    .Take(10)
                    .Select(jp => new MarketJobPostViewModel
                    {
                        JobTitle = jp.JobTitle,
                        CompanyName = jp.CompanyName,
                        Salary = jp.SalaryRange,
                        Level = jp.JobLevel,
                        Location = jp.Location,
                        Link = jp.SourceUrl
                    })
                    .ToListAsync();

                // 6. TÍNH TOÁN ĐIỂM SỐ (Giữ nguyên logic cũ)
                int totalTopSkillsFrequency = topSkillsData.Sum(s => s.Frequency);
                var culture = new CultureInfo("vi-VN");
                var marketSkills = topSkillsData.Select(s => s.SkillName.Trim().ToLower()).ToList();
                var topSkillsForList = topSkillsData.Select(s => culture.TextInfo.ToTitleCase(s.SkillName)).ToList();

                var cvSkills = new List<string>();
                var analysisNode = JsonNode.Parse(cv.AnalysisResult);
                if (analysisNode?["jd_match_analysis"]?["matching_keywords"] is JsonArray matchedKeywords)
                {
                    foreach (var keyword in matchedKeywords) { if (keyword != null) cvSkills.Add(keyword.GetValue<string>()); }
                }
                else if (analysisNode?["categories"] is JsonArray categories)
                {
                    var skillsCategory = categories.FirstOrDefault(c => c?["category_name"]?.GetValue<string>().ToLower().Contains("kỹ năng") ?? false);
                    if (skillsCategory?["criteria"] is JsonArray criteria)
                    {
                        foreach (var criterion in criteria) { if (criterion?["name"] != null) cvSkills.Add(criterion["name"].GetValue<string>()); }
                    }
                }

                var cvSkillsLower = cvSkills.Select(s => s.ToLower()).ToList();
                var matchedSkills = cvSkillsLower.Intersect(marketSkills, StringComparer.OrdinalIgnoreCase).ToList();
                var missingTopSkills = marketSkills.Except(cvSkillsLower, StringComparer.OrdinalIgnoreCase).ToList();

                int fitScore = (marketSkills.Any())
                    ? (int)Math.Round((double)matchedSkills.Count / marketSkills.Count * 100)
                    : 0;

                string summary;
                if (!topSkillsData.Any()) { summary = $"Dữ liệu đang được AI xử lý thêm cho '{jobCategory}'. Vui lòng quay lại sau ít phút."; }
                else if (fitScore >= 70) { summary = $"Rất tốt! CV tương thích cao với ngành '{jobCategory}'."; }
                else if (fitScore >= 40) { summary = $"Khá ổn. Bạn cần bổ sung thêm vài kỹ năng hot."; }
                else { summary = $"Cần cải thiện. Độ tương thích với ngành '{jobCategory}' còn thấp."; }

                // 7. CHUẨN BỊ BIỂU ĐỒ
                var topSkillsChartData = new ChartData();
                var chartDataForChart = topSkillsData.AsEnumerable().Reverse().ToList();
                topSkillsChartData.Labels = chartDataForChart.Select(s => culture.TextInfo.ToTitleCase(s.SkillName)).ToList();
                topSkillsChartData.Data = chartDataForChart.Select(s => s.Frequency).ToList();

                // 8. ĐÓNG GÓI KẾT QUẢ
                var result = new MarketAnalysisResultViewModel
                {
                    MarketFitScore = fitScore,
                    MatchedSkills = matchedSkills.Select(s => culture.TextInfo.ToTitleCase(s)).ToList(),
                    MissingTopSkills = missingTopSkills.Select(s => culture.TextInfo.ToTitleCase(s)).ToList(),
                    Summary = summary,
                    TopMarketSkills = topSkillsForList,
                    TopSkillsChart = topSkillsChartData,
                    RelevantJobs = relevantJobs
                };

                // 9. LỘ TRÌNH HỌC
                if (fitScore < 50 && missingTopSkills.Any())
                {
                    var learningPrompt = $@"Bạn là cố vấn sự nghiệp. Ứng viên ngành '{jobCategory}' thiếu: {string.Join(", ", missingTopSkills)}.
Hãy tạo lộ trình 3 bước ngắn gọn.
Trả về JSON: {{ ""plan"": [ {{ ""title"": ""..."", ""description"": ""..."" }} ] }}";

                    var rawLearningPlan = await CallOpenAI(learningPrompt);
                    var jsonLearningMatch = Regex.Match(rawLearningPlan, @"\{[\s\S]*\}");
                    if (jsonLearningMatch.Success)
                    {
                        var parsedPlan = JsonDocument.Parse(jsonLearningMatch.Value);
                        if (parsedPlan.RootElement.TryGetProperty("plan", out var planArray))
                        {
                            result.LearningPlan = JsonSerializer.Deserialize<List<LearningStepViewModel>>(planArray.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                }
                var marketSkillsAnalysis = topSkillsData.Select(s => new
                {
                    Name = culture.TextInfo.ToTitleCase(s.SkillName),
                    // Logic: Kỹ năng được coi là có nếu chứa nhau (VD: "ReactJS" chứa "React")
                    IsMatched = cvSkillsLower.Any(cvSkill =>
                        s.SkillName.ToLower().Contains(cvSkill) ||
                        cvSkill.Contains(s.SkillName.ToLower())
                    )
                }).ToList();
                return Ok(new
                {
                    result.MarketFitScore,
                    result.MatchedSkills,
                    result.MissingTopSkills,
                    result.Summary,
                    result.TopMarketSkills,
                    result.TopSkillsChart,
                    result.RelevantJobs,
                    result.LearningPlan,

                    // Thêm trường này để Frontend dùng
                    MarketSkillsAnalysis = marketSkillsAnalysis
                });
            
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "[Market Analysis Error] CV ID: {CvId}", id);
                return StatusCode(500, new { message = "Lỗi server khi phân tích thị trường." });
            }
        }


        
        private async Task<(string Category, string Location)> ClassifyJobAndLocationAsync(string cvText)
        {
            var prompt = $@"Dựa vào nội dung CV, hãy xác định NGÀNH NGHỀ CHÍNH và ĐỊA ĐIỂM LÀM VIỆC ƯU TIÊN.
Chỉ trả lời bằng MỘT đối tượng JSON duy nhất.
Giới hạn NGÀNH NGHỀ: 'IT', 'Marketing', 'Kinh doanh', 'Kế toán', 'Nhân sự'.
Giới hạn ĐỊA ĐIỂM: 'TP.HCM', 'Hà Nội', 'Đà Nẵng'.
Nếu không tìm thấy, dùng 'IT' và 'TP.HCM' làm mặc định.

--- NỘI DUNG CV ---
{cvText.Substring(0, Math.Min(cvText.Length, 3000))}
--- KẾT THÚC ---

JSON TRẢ VỀ (ví dụ: {{ ""category"": ""IT"", ""location"": ""Hà Nội"" }}):";

            var aiResultJson = await CallOpenAI(prompt);

            string category = "IT";
            string location = "TP.HCM";

            try
            {
                var match = Regex.Match(aiResultJson, @"\{[\s\S]*\}");
                if (match.Success)
                {
                    var jsonNode = JsonNode.Parse(match.Value);
                    category = jsonNode?["category"]?.GetValue<string>() ?? "IT";
                    location = jsonNode?["location"]?.GetValue<string>() ?? "TP.HCM";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lỗi khi parse JSON phân loại của AI. Dùng mặc định.");
            }

            return (category, location);
        }


        private async Task<string> AnalyzeCvAsync(string cvText, string? jobDescription, string jobCategory)
        {
            var promptBuilder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(jobDescription))
            {
                promptBuilder.AppendLine("Bạn là một chuyên gia tuyển dụng AI hàng đầu. Hãy so sánh CV của ứng viên với JD được cung cấp.");
                promptBuilder.AppendLine("Nhiệm vụ: Phân tích độ phù hợp và tạo ra bộ câu hỏi phỏng vấn SÁT VỚI THỰC TẾ.");
                promptBuilder.AppendLine(@"
        HÃY TRẢ VỀ KẾT QUẢ DƯỚI DẠNG JSON MẪU SAU (KHÔNG MARKDOWN):
        {
          ""applicant_name"": ""(Tên ứng viên AI tự phát hiện)"",
          ""detected_level"": ""(Senior/Mid/Junior)"",
          ""detected_industry"": ""(Ngành nghề)"",
          ""jd_match_analysis"": {
            ""match_score"": 85,
            ""summary"": ""Ứng viên đáp ứng tốt yêu cầu về Java nhưng thiếu kinh nghiệm Cloud."",
            ""matching_keywords"": [""Java"", ""SQL""] ,
            ""missing_keywords"": [""AWS""]
          },
          ""analysis_summary"": ""CV tốt, cần làm rõ thêm về kinh nghiệm thực tế với Cloud."",
          ""categories"": [
            {
              ""category_name"": ""Kinh nghiệm vs JD"", ""score"": 25, ""max_score"": 30,
              ""criteria"": [
                { ""name"": ""Số năm kinh nghiệm"", ""score"": 10, ""max_score"": 10, ""justification"": ""Đủ 3 năm theo yêu cầu."" }
              ]
            },
            {
              ""category_name"": ""Kỹ năng vs JD"", ""score"": 20, ""max_score"": 30,
              ""criteria"": [
                 { ""name"": ""Kỹ năng Tech"", ""score"": 15, ""max_score"": 15, ""justification"": ""Tốt."" }
              ]
            }
          ],
          ""priority_suggestions"": [
            { ""type"": ""ADD_DATA"", ""suggestion"": ""Bổ sung chứng chỉ AWS nếu có."", ""original_text"": null }
          ],
          ""red_flags"": [],
          ""strengths"": [""Java Core vững"", ""Tư duy logic tốt""],
          ""general_suggestions"": [""Cải thiện layout CV.""],
          
          ""interview_questions"": {
            ""strength_question"": ""(Tạo 1 câu hỏi xoáy sâu vào kỹ năng mạnh nhất khớp với JD. Ví dụ: 'Trong dự án A, bạn đã dùng Java để giải quyết vấn đề hiệu năng như thế nào?')"",
            ""weakness_question"": ""(Tạo 1 câu hỏi về kỹ năng còn thiếu so với JD. Ví dụ: 'JD yêu cầu AWS nhưng CV bạn chưa có, bạn dự định bù đắp kiến thức này ra sao?')"",
            ""red_flag_question"": ""(Tạo 1 câu hỏi về điểm bất hợp lý hoặc khoảng trống thời gian. Nếu không có red flag, hãy hỏi về định hướng lâu dài.)""
          }
        }");
            }
           
            else
            {
                promptBuilder.AppendLine($"Bạn là nhà tuyển dụng khó tính đang tuyển vị trí: {jobCategory}.");
                promptBuilder.AppendLine("Nhiệm vụ: Chấm điểm CV, tìm lỗi (red flags) và chuẩn bị câu hỏi phỏng vấn để kiểm tra ứng viên.");
                promptBuilder.AppendLine(@"
        HÃY TRẢ VỀ KẾT QUẢ DƯỚI DẠNG JSON MẪU SAU (KHÔNG MARKDOWN):
        {
          ""applicant_name"": ""(Tên ứng viên)"",
          ""detected_level"": ""(Senior/Mid/Junior)"",
          ""detected_industry"": ""(Ngành nghề)"",
          ""overall_score"": 75,
          ""analysis_summary"": ""CV cấu trúc ổn, nhưng thiếu số liệu định lượng thành tích."",
          ""categories"": [
            {
              ""category_name"": ""Trình bày & ATS"", ""score"": 15, ""max_score"": 20,
              ""criteria"": [
                { ""name"": ""Bố cục"", ""score"": 8, ""max_score"": 10, ""justification"": ""Dễ nhìn."" }
              ]
            },
            {
              ""category_name"": ""Kinh nghiệm"", ""score"": 25, ""max_score"": 40,
              ""criteria"": [
                { ""name"": ""Chất lượng dự án"", ""score"": 20, ""max_score"": 30, ""justification"": ""Mô tả chi tiết."" }
              ]
            },
            {
              ""category_name"": ""Kỹ năng"", ""score"": 20, ""max_score"": 25,
              ""criteria"": [
                 { ""name"": ""Hard Skills"", ""score"": 15, ""max_score"": 15, ""justification"": ""Đầy đủ."" }
              ]
            }
          ],
          ""priority_suggestions"": [
            { ""type"": ""REWRITE"", ""suggestion"": ""Viết lại phần mục tiêu nghề nghiệp rõ ràng hơn."", ""original_text"": ""Tìm việc môi trường tốt"" }
          ],
          ""red_flags"": [
             { ""type"": ""Lỗ hổng thời gian"", ""details"": ""Nghỉ 6 tháng năm 2023"", ""verification_step"": ""Hỏi lý do nghỉ."" }
          ],
          ""strengths"": [""Tiếng Anh tốt""],
          ""general_suggestions"": [""Thêm link Portfolio.""],

          ""interview_questions"": {
            ""strength_question"": ""(Tự tìm điểm mạnh nhất trong CV và đặt câu hỏi tình huống. Ví dụ: 'Bạn ghi là thạo ReactJS, hãy kể về bug khó nhất bạn từng fix với ReactJS tại công ty cũ?')"",
            ""weakness_question"": ""(Tìm điểm yếu nhất trong CV để hỏi. Ví dụ: 'Kinh nghiệm của bạn chủ yếu là Outsourcing, bạn nghĩ sao về việc chuyển sang làm Product?')"",
            ""red_flag_question"": ""(Đặt câu hỏi trực diện về Red Flag tìm thấy. Ví dụ: 'Tại sao bạn lại rời công ty X chỉ sau 3 tháng?')""
          }
        }");
            }

            // --- NẠP DỮ LIỆU ---
            promptBuilder.AppendLine("\n--- NỘI DUNG CV CẦN PHÂN TÍCH ---");
            promptBuilder.AppendLine(cvText);

            if (!string.IsNullOrWhiteSpace(jobDescription))
            {
                promptBuilder.AppendLine("\n--- NỘI DUNG JOB DESCRIPTION ---");
                promptBuilder.AppendLine(jobDescription);
            }

            promptBuilder.AppendLine("--- KẾT THÚC DỮ LIỆU ---");

            promptBuilder.AppendLine(@"
    YÊU CẦU ĐẶC BIỆT QUAN TRỌNG:
    1. Chỉ trả về JSON hợp lệ.
    2. TRONG PHẦN 'interview_questions':
       - TUYỆT ĐỐI KHÔNG để văn bản mẫu như '[Công ty X]', '[Điểm mạnh A]'.
       - PHẢI TỰ ĐIỀN thông tin thật lấy từ CV vào câu hỏi. 
       - Ví dụ: Thay vì nói 'Tại công ty cũ', hãy nói 'Tại công ty FPT Software'.
       - Ví dụ: Thay vì nói 'về kỹ năng này', hãy nói 'về kỹ năng Docker'.
    ");

            return await CallOpenAI(promptBuilder.ToString());
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cv == null) return NotFound();
            return View(cv);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string extractedText)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cv == null) return NotFound();

            cv.ExtractedText = extractedText;
            _context.Update(cv);
            await _context.SaveChangesAsync();
            return RedirectToAction("Edit", new { id = cv.Id });
        }

        [HttpGet]
        public async Task<IActionResult> ExportAsPdf(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cv == null || string.IsNullOrEmpty(cv.ExtractedText))
            {
                return NotFound("Không tìm thấy nội dung CV để xuất file.");
            }

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(2, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(12));
                    page.Header().Text("CV được tạo bởi CV Analyzer").SemiBold().FontSize(10).FontColor(Colors.Grey.Medium);

                    page.Content().PaddingVertical(1, QuestPDF.Infrastructure.Unit.Centimetre).Column(col =>
                    {
                        col.Spacing(20);
                        col.Item().Text(cv.ExtractedText);
                    });
                    page.Footer().AlignCenter().Text(x => { x.Span("Trang "); x.CurrentPageNumber(); });
                });
            });

            byte[] pdfBytes = document.GeneratePdf();
            string fileName = $"CV_{Path.GetFileNameWithoutExtension(cv.FileName)}_{DateTime.Now:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cv != null)
            {
                string filePath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", cv.StoredFileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
                _context.Cvs.Remove(cv);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("History");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BatchUpload(List<IFormFile> cvFiles, int jobPostingId)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));

            var jobPosting = await _context.JobPostings
                .FirstOrDefaultAsync(jp => jp.Id == jobPostingId && jp.UserId == userId);

            if (jobPosting == null)
            {
                return Unauthorized("Bạn không có quyền upload cho vị trí này.");
            }

            if (cvFiles == null || cvFiles.Count == 0)
            {
                TempData["Error"] = "Vui lòng chọn ít nhất một file CV để upload.";
                return RedirectToAction("Dashboard", "JobPosting", new { id = jobPostingId });
            }

            foreach (var file in cvFiles)
            {
                if (file.Length > 5 * 1024 * 1024)
                {
                    continue;
                }

                string uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                Directory.CreateDirectory(uploadsFolder);
                await using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                string? cvText = ExtractTextFromPdfWithPdfPig(filePath);
                if (string.IsNullOrWhiteSpace(cvText))
                {
                    continue;
                }

                string rawAnalysisResponse = await AnalyzeCvAsync(cvText, jobPosting.JobDescriptionText, "IT");

                var jsonMatch = Regex.Match(rawAnalysisResponse, @"\{[\s\S]*\}");
                string analysisJson = jsonMatch.Success ? jsonMatch.Value : "{}";

                var cv = new Cv
                {
                    UserId = userId,
                    FileName = file.FileName,
                    StoredFileName = uniqueFileName,
                    AnalysisResult = analysisJson,
                    ExtractedText = cvText,
                    UploadedAt = DateTime.UtcNow,
                    JobDescriptionText = jobPosting.JobDescriptionText,
                    JobCategory = jobPosting.Title,
                    JobPostingId = jobPostingId
                };

                _context.Cvs.Add(cv);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Đã upload và phân tích thành công {cvFiles.Count} CV.";
            return RedirectToAction("Dashboard", "JobPosting", new { id = jobPostingId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuggestJobs(int id)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

            if (cv == null || string.IsNullOrEmpty(cv.AnalysisResult))
            {
                return NotFound("Không tìm thấy dữ liệu phân tích CV.");
            }

            try
            {
                var cvSkills = new List<string>();
                var analysisNode = JsonNode.Parse(cv.AnalysisResult);
                if (analysisNode?["categories"] is JsonArray categories)
                {
                    var skillsCategory = categories.FirstOrDefault(c => c?["category_name"]?.GetValue<string>().ToLower().Contains("kỹ năng") ?? false);
                    if (skillsCategory?["criteria"] is JsonArray criteria)
                    {
                        foreach (var criterion in criteria)
                        {
                            if (criterion?["name"] != null)
                            {
                                cvSkills.Add(criterion["name"].GetValue<string>());
                            }
                        }
                    }
                }

                if (!cvSkills.Any())
                {
                    return Ok(new { suggestions = "Không thể tìm thấy danh sách kỹ năng trong kết quả phân tích để đưa ra gợi ý." });
                }

                var jobPrompt = $@"Dựa trên các kỹ năng chính sau đây, hãy gợi ý 3 vị trí công việc phù hợp tại Việt Nam và giải thích ngắn gọn tại sao.
                                    --- KỸ NĂNG: {string.Join(", ", cvSkills)} ---";

                var chatRequest = new ChatRequest(new List<Message> { new Message(Role.User, jobPrompt) }, model: "gpt-5.2");
                var response = await _openAIClient.ChatEndpoint.GetCompletionAsync(chatRequest);
                var suggestions = response.FirstChoice.Message.Content.GetString();
                return Ok(new { suggestions });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return StatusCode(500, "Đã có lỗi xảy ra khi xử lý yêu cầu gợi ý công việc.");
            }
        }

        private async Task<string> CallOpenAI(string prompt)
        {
            var messages = new List<Message> { new Message(Role.User, prompt) };
            try
            {
                var chatRequest = new ChatRequest(messages, model: "gpt-5.2");
                var response = await _openAIClient.ChatEndpoint.GetCompletionAsync(chatRequest);
                return response.FirstChoice.Message.Content.GetString().Trim();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[OpenAI API Error] Lỗi khi gọi OpenAI");
                var errorResponse = new { error_message = $"Lỗi khi gọi API OpenAI: {e.Message}", error_details = e.ToString() };
                return JsonSerializer.Serialize(errorResponse);
            }
        }

        private string ExtractTextFromPdfWithPdfPig(string filePath)
        {
            try
            {
                var allText = new StringBuilder();
                using (var document = UglyToad.PdfPig.PdfDocument.Open(filePath))
                {
                    foreach (var page in document.GetPages())
                    {
                        allText.Append(page.Text);
                        allText.Append(" ");
                    }
                }
                return allText.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi trích xuất text từ PDF: {FilePath}", filePath);
                return null;
            }
        }

        private async Task TriggerOnDemandCrawl(string jobCategory, string location)
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler();

                var jobData = new JobDataMap
                {
                    { "JobCategory", jobCategory },
                    { "Location", location }
                };

                await scheduler.TriggerJob(new JobKey("OnDemandCrawlJob"), jobData);

                _logger.LogInformation("Đã kích hoạt OnDemandCrawlJob thành công cho '{Category}' @ '{Location}'.", jobCategory, location);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi kích hoạt On-Demand job cho '{Category}'", jobCategory);
            }
        }


    } // Kết thúc class CvController

    // [MỚI] Class này cần nằm ở đây (cuối namespace) để tránh lỗi "RewriteRequestModel could not be found"
    public class RewriteRequestModel
    {
        public string SuggestionText { get; set; }
    }

} // Kết thúc namespace