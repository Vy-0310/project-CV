using CVAnalyzer.WebApp.Data;
using CVAnalyzer.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using QuestPDF.Infrastructure;
using DinkToPdf;
using DinkToPdf.Contracts;
using System.IO;
using System.Runtime.Loader;

// --- THÊM CÁC USING CẦN THIẾT CHO TÍNH NĂNG MỚI ---
using CVAnalyzer.Data; // <-- Thêm using này (nếu ApplicationDbContext ở đây)
using Quartz; // <-- Thêm using này, sửa lỗi 'ISchedulerFactory'
using CVAnalyzer.Crawler.Services; // <-- Thêm using cho Service
using CVAnalyzer.Crawler.Jobs;     // <-- Thêm using cho Job

QuestPDF.Settings.License = LicenseType.Community;
QuestPDF.Drawing.FontManager.RegisterFont(File.OpenRead("wwwroot/fonts/BeVietnamPro-Regular.ttf"));
var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// === THÊM MỚI: Đăng ký HttpClientFactory để service crawl có thể sử dụng ===
builder.Services.AddHttpClient();

// Add services to the container.
builder.Services.AddControllersWithViews();


builder.Services.AddScoped<IViewRendererService, ViewRendererService>();

var wkHtmlToPdfPath = Path.Combine(builder.Environment.ContentRootPath, "wkhtmltopdf", "bin", "libwkhtmltox.dll");

CustomAssemblyLoadContext context = new CustomAssemblyLoadContext();
try
{
    context.LoadUnmanagedLibrary(wkHtmlToPdfPath);
}
catch (Exception ex)
{
    Console.WriteLine($"[CRITICAL ERROR] Không thể tìm thấy 'libwkhtmltox.dll' tại: {wkHtmlToPdfPath}");
    Console.WriteLine($"Lỗi: {ex.Message}");
}
builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));
// --- KẾT THÚC CẤU HÌNH PDF ---



const string OpenAIHttpClientName = "OpenAIClientWithTimeout";
builder.Services.AddHttpClient(OpenAIHttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(300);
});
builder.Services.AddSingleton(serviceProvider =>
{
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient(OpenAIHttpClientName);
    var apiKey = builder.Configuration["OpenAI:ApiKey"]; // Đảm bảo WebApp/appsettings.json cũng có key
    var openAIAuthentication = new OpenAIAuthentication(apiKey);
    var openAISettings = new OpenAISettings();
    return new OpenAIClient(openAIAuthentication, openAISettings, httpClient);
});




// Cấu hình Authentication bằng Cookie
builder.Services.AddAuthentication("MyCookieAuth").AddCookie("MyCookieAuth", options =>
{
    options.Cookie.Name = "MyCookieAuth";
    options.LoginPath = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/AccessDenied";
});



builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 2. Đăng ký Dịch vụ Ánh xạ (SlugMappingService)
builder.Services.AddSingleton<SlugMappingService>();

// 3. Thêm và cấu hình Quartz cho WebApp
builder.Services.AddQuartz(q =>
{
    
    q.UseMicrosoftDependencyInjectionJobFactory();

    // Đăng ký Job "On-Demand"
    // Chúng ta không đặt lịch (trigger) mà chỉ đăng ký để có thể gọi
    var jobKey = new JobKey("OnDemandCrawlJob");
    q.AddJob<OnDemandCrawlJob>(opts => opts
        .WithIdentity(jobKey)
        .StoreDurably() // <-- QUAN TRỌNG: Cho phép job "nằm chờ" mà không cần lịch
    );
});

// 4. Thêm Quartz Hosted Service (để khởi chạy Quartz)
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = false);



var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();


// --- Lớp Helper để load thư viện C++ (wkhtmltopdf) ---
public class CustomAssemblyLoadContext : AssemblyLoadContext
{
    public IntPtr LoadUnmanagedLibrary(string absolutePath)
    {
        return LoadUnmanagedDll(absolutePath);
    }
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        return LoadUnmanagedDllFromPath(unmanagedDllName);
    }
    protected override System.Reflection.Assembly Load(System.Reflection.AssemblyName assemblyName)
    {
        throw new NotImplementedException();
    }
}