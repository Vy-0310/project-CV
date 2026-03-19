// File: Models/User.cs
namespace CVAnalyzer.WebApp.Models;

public partial class User
{
    public int Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    // --- THUỘC TÍNH Cvs CẦN CÓ Ở ĐÂY ---
    public virtual List<Cv> Cvs { get; set; } = new List<Cv>();
    // -----------------------------------
}