using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CVAnalyzer.Crawler.Models
{
    // Model để hứng kết quả JSON từ AI
    public class SkillExtractionResult
    {
        [JsonPropertyName("skills")]
        public List<string> Skills { get; set; } = new List<string>();
    }
}