using System.Collections.Generic;

namespace CVAnalyzer.WebApp.ViewModels
{
    /// <summary>
    /// Lớp định nghĩa một bước trong lộ trình học tập do AI đề xuất.
    /// </summary>
    public class LearningStepViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// [MỚI] ViewModel hiển thị thông tin công việc kèm Link
    /// </summary>
    public class MarketJobPostViewModel
    {
        public string JobTitle { get; set; }
        public string CompanyName { get; set; }
        public string Salary { get; set; }
        public string Level { get; set; }
        public string Location { get; set; }
        public string Link { get; set; } // Link gốc tới trang tuyển dụng
    }

    public class MarketAnalysisResultViewModel
    {
        public int MarketFitScore { get; set; }
        public List<string> MatchedSkills { get; set; } = new();
        public List<string> MissingTopSkills { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public string JobTitle { get; set; } = "IT tại TP.HCM";
        public List<LearningStepViewModel>? LearningPlan { get; set; }
        public List<string> TopMarketSkills { get; set; } = new();
        public ChartData TopSkillsChart { get; set; }

        // [MỚI] Danh sách các Job phù hợp trả về cho View
        public List<MarketJobPostViewModel> RelevantJobs { get; set; }
    }

    public class MarketDataViewModel
    {
        public List<string> TopRequiredSkills { get; set; } = new();
        public int TotalJobsAnalysed { get; set; }
    }
}