using AngleSharp.Html.Parser;
using CVAnalyzer.Crawler.Models;
using CVAnalyzer.Data.Models;
using CVAnalyzer.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using Quartz;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CVAnalyzer.Crawler.Jobs
{
    [DisallowConcurrentExecution]
    public class ProcessJdJob : IJob
    {
        private readonly ILogger<ProcessJdJob> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly OpenAIClient _openAIClient;

        public ProcessJdJob(ILogger<ProcessJdJob> logger,
                            IDbContextFactory<ApplicationDbContext> dbContextFactory,
                            OpenAIClient openAIClient)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _openAIClient = openAIClient;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("--- Bắt đầu Job xử lý JD thô bằng AI... ---");
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            // 1. Tìm 10 JobPosts chưa được xử lý
            var jobsToProcess = await dbContext.JobPosts
                .Where(jp => !jp.IsProcessedByAI)
                .OrderByDescending(jp => jp.CrawledAt)
                .Take(10) // Xử lý 10 job mỗi lần
                .ToListAsync();

            if (!jobsToProcess.Any())
            {
                _logger.LogInformation("Không có JobPost mới nào cần xử lý.");
                return;
            }

            _logger.LogInformation("Tìm thấy {count} JobPosts cần xử lý AI.", jobsToProcess.Count);
            var htmlParser = new HtmlParser();

            // 2. Lặp qua từng JobPost
            foreach (var jobPost in jobsToProcess)
            {
                try
                {
                    // 2a. Làm sạch HTML thô thành Text
                    string jdText = CleanHtml(htmlParser, jobPost.FullDescriptionText);
                    string reqText = CleanHtml(htmlParser, jobPost.RequirementsText);
                    string fullText = jdText + "\n" + reqText;

                    if (string.IsNullOrWhiteSpace(fullText))
                    {
                        jobPost.IsProcessedByAI = true; // Đánh dấu đã xử lý (vì không có nội dung)
                        await dbContext.SaveChangesAsync();
                        continue;
                    }

                    // 2b. Gọi AI
                    string prompt = BuildPrompt(fullText);
                    var aiResultJson = await CallOpenAI(prompt);

                    // 2c. Parse kết quả JSON
                    var extractionResult = JsonSerializer.Deserialize<SkillExtractionResult>(aiResultJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (extractionResult != null && extractionResult.Skills.Any())
                    {
                        // 2d. Lưu kỹ năng vào JobPostSkill
                        foreach (var skillName in extractionResult.Skills.Distinct()) // Chỉ lấy kỹ năng duy nhất
                        {
                            dbContext.JobPostSkills.Add(new JobPostSkill
                            {
                                JobPostId = jobPost.Id,
                                SkillName = skillName.Trim(),
                                SkillType = "Technical" 
                            });
                        }
                        _logger.LogInformation(" -> Đã trích xuất {count} kỹ năng cho Job ID: {JobId}", extractionResult.Skills.Count, jobPost.Id);
                    }

                    // 2e. Đánh dấu JobPost này là đã xử lý
                    jobPost.IsProcessedByAI = true;
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi xử lý AI cho Job ID: {JobId}", jobPost.Id);
                    jobPost.IsProcessedByAI = true; // Đánh dấu lỗi để không thử lại
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        private string CleanHtml(HtmlParser parser, string? htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent)) return "";
            var document = parser.ParseDocument(htmlContent);
            return document.Body?.TextContent ?? "";
        }

        private string BuildPrompt(string jobText)
        {
            return $@"Dựa trên Mô tả Công việc (JD) sau, hãy trích xuất 10-15 kỹ năng (skills) quan trọng nhất.
CHỈ TRẢ VỀ một đối tượng JSON duy nhất có cấu trúc sau: {{ ""skills"": [""skill1"", ""skill2"", ...] }}

--- JD TEXT ---
{jobText.Substring(0, Math.Min(jobText.Length, 3500))}
--- END JD TEXT ---

JSON:";
        }

        private async Task<string> CallOpenAI(string prompt)
        {
            var messages = new List<Message> { new Message(Role.User, prompt) };
            try
            {
                var chatRequest = new ChatRequest(messages, model: "gpt-4o");
                var response = await _openAIClient.ChatEndpoint.GetCompletionAsync(chatRequest);
                var rawJson = response.FirstChoice.Message.Content.GetString().Trim();

                // Đảm bảo kết quả là JSON
                var match = Regex.Match(rawJson, @"\{[\s\S]*\}");
                if (match.Success) return match.Value;

                return "{ \"skills\": [] }"; // Trả về JSON rỗng nếu AI không trả về đúng
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[OpenAI API Error] Lỗi khi gọi OpenAI");
                return "{ \"skills\": [] }";
            }
        }
    }
}