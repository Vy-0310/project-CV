using CVAnalyzer.WebApp.Data;
using CVAnalyzer.WebApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using System.Net.Http.Headers; // [MỚI] Dùng cho HTTP Client
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CVAnalyzer.WebApp.Controllers
{
    [Authorize]
    public class InterviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly OpenAIClient _openAIClient;
        private readonly IConfiguration _configuration; // [MỚI] Để lấy API Key

        public InterviewController(ApplicationDbContext context, OpenAIClient openAIClient, IConfiguration configuration)
        {
            _context = context;
            _openAIClient = openAIClient;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> Start(int cvId)
        {
            var userId = int.Parse(User.FindFirstValue("UserId"));
            var cv = await _context.Cvs.FirstOrDefaultAsync(c => c.Id == cvId && c.UserId == userId);
            if (cv == null) return NotFound();

            var analysisNode = JsonNode.Parse(cv.AnalysisResult ?? "{}");
            var cvSummary = analysisNode?["analysis_summary"]?.GetValue<string>() ?? "Không có tóm tắt";

            // --- TRÍCH XUẤT KỸ NĂNG ---
            var skillsList = new List<string>();
            var categories = analysisNode?["categories"]?.AsArray();
            if (categories != null)
            {
                var skillsCategory = categories.FirstOrDefault(c =>
                    c?["category_name"]?.GetValue<string>().ToLower().Contains("kỹ năng") == true ||
                    c?["category_name"]?.GetValue<string>().ToLower().Contains("skills") == true
                );
                if (skillsCategory?["criteria"] is JsonArray criteriaArr)
                {
                    foreach (var item in criteriaArr)
                    {
                        var name = item?["name"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name)) skillsList.Add(name);
                    }
                }
            }
            if (!skillsList.Any() && analysisNode?["jd_match_analysis"]?["matching_keywords"] is JsonArray keywordsArr)
            {
                foreach (var kw in keywordsArr)
                {
                    var val = kw?.GetValue<string>();
                    if (!string.IsNullOrEmpty(val)) skillsList.Add(val);
                }
            }

            var topSkills = skillsList.Any() ? string.Join(", ", skillsList.Take(10)) : "kỹ năng chuyên môn";
            var candidateName = analysisNode?["applicant_name"]?.GetValue<string>() ?? "Ứng viên";
            var jobCategory = analysisNode?["detected_industry"]?.GetValue<string>() ?? cv.JobCategory ?? "General";

            // Prompt mở đầu
            var prompt = $"Bạn là nhà tuyển dụng. Tôi là {candidateName}, ứng tuyển {jobCategory}. " +
                         $"Kỹ năng: {topSkills}. Tóm tắt: {cvSummary}. " +
                         $"Hãy đặt 1 câu hỏi mở đầu ngắn gọn để tôi giới thiệu bản thân. Chỉ trả về nội dung câu hỏi.";

            var firstQuestion = await CallChatOpenAI(prompt, false);

            var model = new InterviewSessionViewModel
            {
                CvId = cvId,
                CvFileName = cv.FileName,
                FirstQuestion = firstQuestion,
                CandidateName = candidateName,
                JobCategory = jobCategory
            };

            return View("Session", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitAnswer([FromBody] InterviewTurnViewModel model)
        {
            if (model == null || model.History == null || !model.History.Any())
                return BadRequest(new { feedback = "Lỗi dữ liệu", nextQuestion = "Vui lòng tải lại trang." });

            var promptBuilder = new StringBuilder();

            // --- LOGIC PROMPT THÔNG MINH ---
            promptBuilder.AppendLine("Bạn là 'AI Interviewer' - Trợ lý phỏng vấn ảo. Mục tiêu: Giúp ứng viên luyện tập.");
            promptBuilder.AppendLine("Dưới đây là lịch sử hội thoại:");

            var recentHistory = model.History.TakeLast(10).ToList();
            foreach (var message in recentHistory)
            {
                promptBuilder.AppendLine($"{message.Role}: {message.Content}");
            }

            promptBuilder.AppendLine("\nNHIỆM VỤ (Trả về JSON):");
            promptBuilder.AppendLine("1. Phân tích câu trả lời mới nhất của User:");
            promptBuilder.AppendLine("   - Nếu trả lời tốt: Đóng vai HR, nhận xét và hỏi tiếp.");
            promptBuilder.AppendLine("   - Nếu User HỎI NGƯỢC LẠI hoặc THẮC MẮC (VD: 'Tại sao hỏi vậy?'): Hãy GIẢI THÍCH lý do bạn hỏi (dựa trên JD/Kỹ năng), ĐỪNG chê họ. Sau đó đổi câu hỏi khác.");
            promptBuilder.AppendLine("   - Nếu User nói 'Không biết': Hãy động viên và gợi ý.");

            promptBuilder.AppendLine("2. Output JSON fields:");
            promptBuilder.AppendLine("   - 'feedback': Nhận xét (Tiếng Việt).");
            promptBuilder.AppendLine("   - 'nextQuestion': Câu hỏi tiếp theo (Tiếng Việt).");
            promptBuilder.AppendLine("   - 'sentiment': Đánh giá ('positive', 'neutral', 'negative').");

            var responseJson = await CallChatOpenAI(promptBuilder.ToString(), true);

            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                var feedback = root.TryGetProperty("feedback", out var fb) ? fb.GetString() : "Đã ghi nhận.";
                var nextQuestion = root.TryGetProperty("nextQuestion", out var nq) ? nq.GetString() : "Mời bạn tiếp tục.";
                var sentiment = root.TryGetProperty("sentiment", out var st) ? st.GetString() : "neutral";

                return Ok(new { feedback, nextQuestion, sentiment });
            }
            catch
            {
                return Ok(new { feedback = "AI đang bận.", nextQuestion = "Mời bạn chia sẻ tiếp.", sentiment = "neutral" });
            }
        }

        // ==========================================
        // KHU VỰC XỬ LÝ AUDIO BẰNG HTTP CLIENT (ĐỂ TRÁNH LỖI THƯ VIỆN)
        // ==========================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TranscribeAudio(IFormFile audioFile)
        {
            if (audioFile == null || audioFile.Length == 0) return BadRequest(new { error = "File lỗi." });

            // 1. Lấy API Key từ Config
            var apiKey = _configuration["OpenAISettings:ApiKey"] ?? _configuration["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey)) return StatusCode(500, new { error = "Chưa cấu hình API Key." });

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using var content = new MultipartFormDataContent();
                using var audioStream = audioFile.OpenReadStream();

                // OpenAI yêu cầu file phải có tên và đuôi mở rộng
                var fileContent = new StreamContent(audioStream);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm");
                content.Add(fileContent, "file", "input.webm");
                content.Add(new StringContent("whisper-1"), "model");

                var response = await client.PostAsync("https://api.openai.com/v1/audio/transcriptions", content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, new { error = responseString });

                // Parse kết quả: { "text": "..." }
                using var doc = JsonDocument.Parse(responseString);
                var text = doc.RootElement.GetProperty("text").GetString();

                return Ok(new { text });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TextToSpeech([FromBody] TtsRequestModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Text)) return BadRequest("Text trống.");

            var apiKey = _configuration["OpenAISettings:ApiKey"] ?? _configuration["OpenAI:ApiKey"];
            if (string.IsNullOrEmpty(apiKey)) return StatusCode(500, "Chưa cấu hình API Key.");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new
                {
                    model = "tts-1",
                    input = model.Text,
                    voice = "alloy" // Giọng đọc: alloy, echo, fable, onyx, nova, shimmer
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.openai.com/v1/audio/speech", jsonContent);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, "Lỗi từ OpenAI TTS.");

                var bytes = await response.Content.ReadAsByteArrayAsync();
                return File(bytes, "audio/mpeg");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        // Helper dùng thư viện OpenAI cho Chat (vì phần này đang chạy ổn)
        private async Task<string> CallChatOpenAI(string prompt, bool expectJson)
        {
            var messages = new List<Message> { new Message(Role.User, prompt) };
            try
            {
                var chatRequest = new ChatRequest(messages, model: "gpt-5.2");
                var response = await _openAIClient.ChatEndpoint.GetCompletionAsync(chatRequest);
                var rawResponse = response.FirstChoice.Message.Content.GetString().Trim();

                if (expectJson)
                {
                    var jsonMatch = Regex.Match(rawResponse, @"\{[\s\S]*\}");
                    if (jsonMatch.Success) return jsonMatch.Value;
                }
                return rawResponse;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Chat Error: {e.Message}");
                return expectJson
                    ? "{ \"feedback\": \"Hệ thống AI đang bận.\", \"nextQuestion\": \"Thử lại nhé.\", \"sentiment\": \"neutral\" }"
                    : "Hệ thống đang bận.";
            }
        }
    }

    public class TtsRequestModel
    {
        public string Text { get; set; }
    }
}