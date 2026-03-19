using System.Collections.Generic;

namespace CVAnalyzer.WebApp.ViewModels
{
    // ViewModel chính cho trang Dashboard
    public class DashboardViewModel
    {
        public ChartData TopSkillsItHcm { get; set; }
        public ChartData TopSkillsMarketingHn { get; set; }
        public ChartData CategoryComparison { get; set; }
    }
}