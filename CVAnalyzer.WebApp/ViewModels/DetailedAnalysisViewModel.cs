// File: ViewModels/DetailedAnalysisViewModel.cs
using System.Text.Json.Serialization;

namespace CVAnalyzer.WebApp.ViewModels
{
    // Lớp định nghĩa một "cờ đỏ"
    public class RedFlagViewModel
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("details")]
        public string? Details { get; set; }

        [JsonPropertyName("verification_step")]
        public string? VerificationStep { get; set; }
    }

    // Lớp định nghĩa một tiêu chí chấm điểm nhỏ
    public class ScoreCriterion
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("max_score")]
        public int MaxScore { get; set; }

        [JsonPropertyName("justification")]
        public string? Justification { get; set; }
    }

    // Lớp định nghĩa một hạng mục lớn
    public class AnalysisCategory
    {
        [JsonPropertyName("category_name")]
        public string? CategoryName { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("max_score")]
        public int MaxScore { get; set; }

        [JsonPropertyName("criteria")]
        public List<ScoreCriterion>? Criteria { get; set; }
    }

    public class ActionVerbAnalysis
    {
        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("examples")]
        public List<string>? Examples { get; set; }
    }

    public class QuantifiableResultsAnalysis
    {
        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        [JsonPropertyName("examples")]
        public List<string>? Examples { get; set; }
    }

    public class DeepAnalysisViewModel
    {
        [JsonPropertyName("action_verb_usage")]
        public ActionVerbAnalysis? ActionVerbUsage { get; set; }

        [JsonPropertyName("quantifiable_results")]
        public QuantifiableResultsAnalysis? QuantifiableResults { get; set; }
    }

    // ViewModel chính, chứa toàn bộ kết quả
    public class DetailedAnalysisViewModel
    {
        [JsonPropertyName("overall_score")]
        public int OverallScore { get; set; }

        [JsonPropertyName("analysis_summary")]
        public string? AnalysisSummary { get; set; }

        [JsonPropertyName("categories")]
        public List<AnalysisCategory>? Categories { get; set; }

        [JsonPropertyName("priority_suggestions")]
        public List<string>? PrioritySuggestions { get; set; }

        [JsonPropertyName("red_flags")]
        public List<RedFlagViewModel>? RedFlags { get; set; }

        [JsonPropertyName("deep_analysis")]
        public DeepAnalysisViewModel? DeepAnalysis { get; set; }

    }
}