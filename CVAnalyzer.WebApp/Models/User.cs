using System.ComponentModel.DataAnnotations;
using System.Collections.Generic; 

namespace CVAnalyzer.WebApp.Models;

public partial class User
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public virtual List<Cv> Cvs { get; set; } = new List<Cv>();

    // --- THÊM DÒNG NÀY ĐỂ LIÊN KẾT VỚI JOB POSTING ---
    public virtual List<JobPosting> JobPostings { get; set; } = new List<JobPosting>();
    // --- KẾT THÚC THÊM ---

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "User"; // Giữ nguyên vai trò
}