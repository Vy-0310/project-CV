namespace CVAnalyzer.WebApp.Services
{
    public interface IViewRendererService
    {
        Task<string> RenderToStringAsync(string viewName, object model);
    }
}