using System.ComponentModel.DataAnnotations.Schema;

namespace CVAnalyzer.WebApp.Models;

public partial class Cv
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; } = null!;
    public string StoredFileName { get; set; } = null!;
    public DateTime UploadedAt { get; set; }
    public string? AnalysisResult { get; set; }
    public string? ExtractedText { get; set; }

    // ĐẢM BẢO CÓ ĐỦ CÁC THUỘC TÍNH NÀY
    [Column(TypeName = "TEXT")]
    public string? JobDescriptionText { get; set; }
    public string? JobCategory { get; set; }
    public int? MatchScore { get; set; }
    public string? MatchingSkills { get; set; }
    public string? MissingSkills { get; set; }
    public int? ReliabilityScore { get; set; }
    public string? RedFlags { get; set; }

    public int? JobPostingId { get; set; }

    [ForeignKey("JobPostingId")]
    public string? Location { get; set; }
    public virtual JobPosting JobPosting { get; set; }
    public virtual User User { get; set; } = null!;

}
