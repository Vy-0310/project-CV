using CVAnalyzer.Crawler.Jobs;
using CVAnalyzer.Data;
using CVAnalyzer.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using Quartz;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var connectionString = hostContext.Configuration.GetConnectionString("DefaultConnection");

        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        // === THÊM OPENAI CHO CRAWLER ===
        const string OpenAIHttpClientName = "OpenAIClientWithTimeout";
        services.AddHttpClient(OpenAIHttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(300);
        });
        services.AddSingleton(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(OpenAIHttpClientName);
            var apiKey = hostContext.Configuration["OpenAI:ApiKey"]; // Sửa ở đây
            var openAIAuthentication = new OpenAIAuthentication(apiKey);
            var openAISettings = new OpenAISettings();
            return new OpenAIClient(openAIAuthentication, openAISettings, httpClient);
        });
        // === KẾT THÚC THÊM ===

        services.AddQuartz(q =>
        {
            q.UseMicrosoftDependencyInjectionJobFactory();

            var topCvJobKey = new JobKey("TopCvCrawlJob");
            q.AddJob<TopCvCrawlJob>(opts => opts.WithIdentity(topCvJobKey));
            q.AddTrigger(opts => opts
                .ForJob(topCvJobKey)
                .WithIdentity("TopCvCrawlJob-trigger")
                .WithSimpleSchedule(s => s.WithIntervalInHours(6).RepeatForever())
                .StartNow());

            var aiJobKey = new JobKey("ProcessJdJob");
            q.AddJob<ProcessJdJob>(opts => opts.WithIdentity(aiJobKey));
            q.AddTrigger(opts => opts
                .ForJob(aiJobKey)
                .WithIdentity("ProcessJdJob-trigger")
                .WithSimpleSchedule(s => s.WithIntervalInMinutes(10).RepeatForever()) // Chạy 10 phút/lần
                .StartNow());
        });

        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
    })
    .Build();

await host.RunAsync();