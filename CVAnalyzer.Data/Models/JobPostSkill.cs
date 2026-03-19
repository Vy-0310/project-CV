namespace CVAnalyzer.Data.Models
{
    public class JobPostSkill
    {
        public int Id { get; set; }
        public int JobPostId { get; set; } // Khóa ngoại
        public string SkillName { get; set; } // Tên kỹ năng (AI sẽ trích xuất)
        public string? SkillType { get; set; } // "Technical" hoặc "Softskill"

        // Mối quan hệ: Một kỹ năng thuộc về một JobPost
        public virtual JobPost JobPost { get; set; }
    }
}