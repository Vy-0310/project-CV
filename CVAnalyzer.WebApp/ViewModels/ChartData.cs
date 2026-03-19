using System.Collections.Generic;

namespace CVAnalyzer.WebApp.ViewModels
{
    // Dùng để chứa dữ liệu cho một biểu đồ
    public class ChartData
    {
        public List<string> Labels { get; set; } = new List<string>();
        public List<int> Data { get; set; } = new List<int>();
    }
}