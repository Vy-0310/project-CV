using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace CVAnalyzer.Data.Models
{
    public class JobPost
    {
        public int Id { get; set; }
        public string SourceUrl { get; set; } // Link gốc của tin tuyển dụng
        public string JobTitle { get; set; } // "Senior .NET Developer"
        public string CompanyName { get; set; }
        public string? SalaryRange { get; set; } // "20-40tr"
        public string? JobLevel { get; set; } // "Senior"
        public string? Location { get; set; } // "TP.HCM"
        public string JobCategory { get; set; } // "IT" (Từ CrawlTarget)

        [Column(TypeName = "MEDIUMTEXT")]
        public string? FullDescriptionText { get; set; } // Toàn bộ JD thô

        [Column(TypeName = "MEDIUMTEXT")]
        public string? RequirementsText { get; set; } // Toàn bộ Yêu cầu thô

        public bool IsProcessedByAI { get; set; } = false; // Cờ cho Giai đoạn 3
        public DateTime CrawledAt { get; set; }

        // Mối quan hệ: Một JobPost có nhiều kỹ năng
        public virtual List<JobPostSkill> Skills { get; set; } = new List<JobPostSkill>();
    }
}