using AngleSharp;
using CVAnalyzer.Data.Models;
using CVAnalyzer.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Quartz;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CVAnalyzer.Crawler.Jobs
{
    public class CrawlTarget
    {
        public string KeywordSlug { get; set; }
        public string LocationSlug { get; set; }
        public string JobCategory { get; set; }
        public string Location { get; set; }
        public string BuildUrl()
        {
            return $"https://www.topcv.vn/tim-viec-lam-{KeywordSlug}-tai-{LocationSlug}";
        }
    }
    [DisallowConcurrentExecution]
    public class TopCvCrawlJob : IJob
    {
        private readonly ILogger<TopCvCrawlJob> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public TopCvCrawlJob(ILogger<TopCvCrawlJob> logger, IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var targets = new List<CrawlTarget>
            {
                new CrawlTarget { KeywordSlug = "it", LocationSlug = "ho-chi-minh-kl2", JobCategory = "IT", Location = "TP.HCM" },
                new CrawlTarget { KeywordSlug = "it", LocationSlug = "ha-noi-kl1", JobCategory = "IT", Location = "Hà Nội" },
                new CrawlTarget { KeywordSlug = "marketing", LocationSlug = "ho-chi-minh-kl1", JobCategory = "Marketing", Location = "TP.HCM" },
            };

            _logger.LogInformation("--- Bắt đầu phiên DEEP CRAWL cho {count} mục tiêu lúc: {time} ---", targets.Count, DateTimeOffset.Now);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

            foreach (var target in targets)
            {
                var jobDetailUrls = new HashSet<string>();
                try
                {
                    _logger.LogInformation("Đang lấy link cho: '{Category}' @ '{Location}'", target.JobCategory, target.Location);
                    
                    var config = Configuration.Default.WithDefaultLoader();
                    var browsingContext = BrowsingContext.New(config);
                    var document = await browsingContext.OpenAsync(target.BuildUrl());

                    var linkNodes = document.QuerySelectorAll("h3.title a, a.job-item-title, a.job-item-link"); 

                    foreach (var node in linkNodes)
                    {
                        var url = node.GetAttribute("href");
                        if (!string.IsNullOrEmpty(url) && !url.Contains("brand"))
                        {
                            jobDetailUrls.Add(url);
                        }
                    }
                    _logger.LogInformation(" -> Đã tìm thấy {count} links.", jobDetailUrls.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi lấy link cho '{Category}'", target.JobCategory);
                    continue; 
                }

                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 3 };
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
                        
                        // Chờ một trong các selector chung của các layout
                        await page.WaitForSelectorAsync("div.job-detail__info, div.box-info-job, div.job-detail_body, div.job-detail-content", 
                            new PageWaitForSelectorOptions { Timeout = 15000 });

                        // Lấy Tiêu đề
                        var jobTitle = "N/A";
                        var titleLocator = page.Locator("h1.job-detail__info--title, h1.job-title-detail, h2.job-detail__information-detail--title");
                        if (await titleLocator.CountAsync() > 0)
                        {
                            jobTitle = await titleLocator.First.TextContentAsync() ?? "N/A";
                        }
                        
                        // Lấy Tên công ty
                        string companyName = "N/A";
                        var companyLocator = page.Locator("a.job-detail__info--company-name-link, h2.company-name-detail a, a.company, div.company-name-label, a.name");
                        if (await companyLocator.CountAsync() > 0)
                        {
                            companyName = await companyLocator.First.TextContentAsync() ?? "N/A";
                        }

                        // Lấy Mức Lương
                        string? salaryRange = null;
                        var salaryLocator = page.Locator(
                            "div.job-detail__info--section-content:has(div.job-detail__info--section-content-title:text-is('Mức lương')) div.job-detail__info--section-content-value," +
                            "div.box-info-job div:has(span:text-is('Mức lương')) span.value," +
                            "div.job-salary"+
                            "div.job-detail__info--section-content-value"
                        );
                        if (await salaryLocator.CountAsync() > 0)
                        {
                            salaryRange = await salaryLocator.First.TextContentAsync();
                        }
                        
                        // Lấy Cấp Bậc
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

                        
                        string? fullDescription = null;
                        // Thử lấy box nội dung chính của 3 layout
                        var contentLocator = page.Locator("div.job-detail_body, div.job-detail__body, div.job-detail-content, div.jobdetail__information-container");
                        if(await contentLocator.CountAsync() > 0)
                        {
                            fullDescription = await contentLocator.First.InnerHTMLAsync();
                        }
                        
                        string? requirements = null; // Bỏ qua, AI sẽ tự xử lý

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
                            RequirementsText = requirements, // Sẽ là NULL
                            IsProcessedByAI = false,
                            CrawledAt = DateTime.UtcNow
                        };

                        dbContext.JobPosts.Add(jobPost);
                        await dbContext.SaveChangesAsync();
                        
                        _logger.LogInformation(" -> Đã crawl sâu & lưu: {title}", jobPost.JobTitle);
                        await page.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Lỗi khi crawl sâu link: {url}. Bỏ qua.", jobUrl);
                        if (page != null) await page.CloseAsync();
                    }
                });
            } 
        }
    }
}