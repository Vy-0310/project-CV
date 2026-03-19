using AngleSharp;
using AngleSharp.Html.Parser;
using CVAnalyzer.Crawler.Services;
using CVAnalyzer.Data.Models;
using CVAnalyzer.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using OpenAI;
using OpenAI.Chat;
using Quartz;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CVAnalyzer.Crawler.Models;

namespace CVAnalyzer.Crawler.Jobs
{
    [DisallowConcurrentExecution]
    public class OnDemandCrawlJob : IJob
    {
        private readonly ILogger<OnDemandCrawlJob> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly SlugMappingService _slugMappingService;
        private readonly OpenAIClient _openAIClient;
        private readonly IHtmlParser _htmlParser;

        public OnDemandCrawlJob(ILogger<OnDemandCrawlJob> logger,
                                IDbContextFactory<ApplicationDbContext> dbContextFactory,
                                SlugMappingService slugMappingService,
                                OpenAIClient openAIClient)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _slugMappingService = slugMappingService;
            _openAIClient = openAIClient;
            _htmlParser = new HtmlParser();
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var jobDataMap = context.JobDetail.JobDataMap;
            string jobCategory = jobDataMap.GetString("JobCategory") ?? "Unknown";
            string location = jobDataMap.GetString("Location") ?? "Unknown";

            _logger.LogInformation("[On-Demand] Bắt đầu chạy cho: '{Category}' @ '{Location}'", jobCategory, location);

            await using var dbCheck = await _dbContextFactory.CreateDbContextAsync();
            var oneDayAgo = DateTime.UtcNow.AddDays(-1);
            var latestTimestamp = await dbCheck.JobPosts
                                        .Where(jp => jp.JobCategory == jobCategory && jp.Location == location)
                                        .MaxAsync(jp => (DateTime?)jp.CrawledAt);

            if (latestTimestamp.HasValue && latestTimestamp.Value > oneDayAgo)
            {
                _logger.LogWarning("[On-Demand] Dữ liệu cho '{Category}' @ '{Location}' vẫn còn mới. Bỏ qua.", jobCategory, location);
                return;
            }

            var (categorySlug, locationSlug) = _slugMappingService.GetSlugs(jobCategory, location);
            if (string.IsNullOrEmpty(categorySlug) || string.IsNullOrEmpty(locationSlug))
            {
                _logger.LogError("[On-Demand] Không tìm thấy Slug mapping. Dừng.");
                return;
            }

            var target = new CrawlTarget
            {
                KeywordSlug = categorySlug,
                LocationSlug = locationSlug,
                JobCategory = jobCategory,
                Location = location
            };

            var jobDetailUrls = new HashSet<string>();
            try
            {
                var config = Configuration.Default.WithDefaultLoader();
                var browsingContext = BrowsingContext.New(config);
                var document = await browsingContext.OpenAsync(target.BuildUrl());
                var linkNodes = document.QuerySelectorAll("h3.title a, a.job-item-title, a.job-item-link"); // Thử nhiều selector

                foreach (var node in linkNodes)
                {
                    var url = node.GetAttribute("href");
                    if (!string.IsNullOrEmpty(url) && !url.Contains("brand"))
                    {
                        jobDetailUrls.Add(url);
                    }
                }
                _logger.LogInformation("[On-Demand] Đã tìm thấy {count} links ở trang 1.", jobDetailUrls.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[On-Demand] Lỗi khi lấy link (Shallow).");
                return;
            }

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 3 };
            int newJobsProcessed = 0;

            await Parallel.ForEachAsync(jobDetailUrls, parallelOptions, async (jobUrl, cancellationToken) =>
            {
                IPage? page = null;
                try
                {
                    await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

                    if (await dbContext.JobPosts.AnyAsync(jp => jp.SourceUrl == jobUrl)) { return; }

                    await using var browserContext = await browser.NewContextAsync(new() { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36" });
                    page = await browserContext.NewPageAsync();
                    await page.GotoAsync(jobUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });

                    // === BẮT ĐẦU LOGIC CRAWL SIÊU LINH HOẠT V5 (Chiến lược mới) ===
                    await page.WaitForSelectorAsync("div.job-detail__info, div.box-info-job, div.job-detail_body, div.job-detail-content",
                        new PageWaitForSelectorOptions { Timeout = 15000 });

                    var jobTitle = "N/A";
                    var titleLocator = page.Locator("h1.job-detail__info--title, h1.job-title-detail, h2.job-detail__information-detail--title");
                    if (await titleLocator.CountAsync() > 0)
                    {
                        jobTitle = await titleLocator.First.TextContentAsync() ?? "N/A";
                    }

                    string companyName = "N/A";
                    var companyLocator = page.Locator("a.job-detail__info--company-name-link, h2.company-name-detail a, a.company");
                    if (await companyLocator.CountAsync() > 0)
                    {
                        companyName = await companyLocator.First.TextContentAsync() ?? "N/A";
                    }

                    string? salaryRange = null;
                    var salaryLocator = page.Locator(
                        "div.job-detail__info--section-content:has(div.job-detail__info--section-content-title:text-is('Mức lương')) div.job-detail__info--section-content-value," +
                        "div.box-info-job div:has(span:text-is('Mức lương')) span.value," +
                        "div.job-salary"
                    );
                    if (await salaryLocator.CountAsync() > 0)
                    {
                        salaryRange = await salaryLocator.First.TextContentAsync();
                    }

                    string? jobLevel = null;
                    var levelLocator = page.Locator(
                        "div.job-detail__info--section-content:has(div.job-detail__info--section-content-title:text-is('Cấp bậc')) span.value," +
                        "div.box-info-job div:has(span:text-is('Cấp bậc')) span.value," +
                        "div.job-level"
                    );
                    if (await levelLocator.CountAsync() > 0)
                    {
                        jobLevel = await levelLocator.First.TextContentAsync();
                    }

                    // --- THAY ĐỔI CHIẾN LƯỢC: LẤY TOÀN BỘ TEXT ---
                    string? fullDescription = null;
                    var contentLocator = page.Locator("div.job-detail_body, div.job-detail__body, div.job-detail-content");
                    if (await contentLocator.CountAsync() > 0)
                    {
                        fullDescription = await contentLocator.First.InnerHTMLAsync();
                    }
                    // --- KẾT THÚC THAY ĐỔI ---

                    await page.CloseAsync();

                    var jobPost = new JobPost
                    {
                        SourceUrl = jobUrl,
                        JobTitle = jobTitle.Trim(),
                        CompanyName = companyName.Trim(),
                        SalaryRange = salaryRange?.Trim(),
                        JobLevel = jobLevel?.Trim(),
                        Location = target.Location,
                        JobCategory = target.JobCategory,
                        FullDescriptionText = fullDescription,
                        RequirementsText = null, // Bỏ qua
                        CrawledAt = DateTime.UtcNow,
                        IsProcessedByAI = false
                    };

                    string jdText = CleanHtml(_htmlParser, jobPost.FullDescriptionText);

                    if (string.IsNullOrWhiteSpace(jdText))
                    {
                        _logger.LogWarning("[On-Demand] Job rỗng, bỏ qua: {url}", jobUrl);
                        return;
                    }

                    string prompt = BuildPrompt(jdText); // Chỉ dùng FullDescription
                    var aiResultJson = await CallOpenAI(prompt);

                    var extractionResult = JsonSerializer.Deserialize<SkillExtractionResult>(aiResultJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (extractionResult != null && extractionResult.Skills.Any())
                    {
                        foreach (var skillName in extractionResult.Skills.Distinct())
                        {
                            jobPost.Skills.Add(new JobPostSkill
                            {
                                SkillName = skillName.Trim(),
                                SkillType = "Technical"
                            });
                        }
                    }

                    jobPost.IsProcessedByAI = true;
                    dbContext.JobPosts.Add(jobPost);
                    await dbContext.SaveChangesAsync();

                    Interlocked.Increment(ref newJobsProcessed);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[On-Demand] Lỗi khi crawl sâu link: {url}.", jobUrl);
                    if (page != null) await page.CloseAsync();
                }
            });

            _logger.LogInformation("[On-Demand] Hoàn thành! Đã xử lý {count} tin tuyển dụng mới.", newJobsProcessed);
        }

        // === CÁC HÀM HELPER (Copy từ ProcessJdJob) ===
        private string CleanHtml(IHtmlParser parser, string? htmlContent)
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
                var chatRequest = new ChatRequest(messages, model: "gpt-5");
                var response = await _openAIClient.ChatEndpoint.GetCompletionAsync(chatRequest);
                var rawJson = response.FirstChoice.Message.Content.GetString().Trim();

                var match = Regex.Match(rawJson, @"\{[\s\S]*\}");
                if (match.Success) return match.Value;
                return "{ \"skills\": [] }";
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[OpenAI API Error] Lỗi khi gọi OpenAI (On-Demand)");
                return "{ \"skills\": [] }";
            }
        }
    }
}