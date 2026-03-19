using CVAnalyzer.Data.Models;
using CVAnalyzer.WebApp.Models;
using Microsoft.EntityFrameworkCore;

namespace CVAnalyzer.WebApp.Data;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // --- DANH SÁCH CÁC BẢNG ---
    public virtual DbSet<Cv> Cvs { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public DbSet<JobPost> JobPosts { get; set; }
    public DbSet<JobPostSkill> JobPostSkills { get; set; }
    public DbSet<JobPosting> JobPostings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_0900_ai_ci").HasCharSet("utf8mb4");

        // === CẤU HÌNH BẢNG: User ===
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("users");
            entity.HasIndex(e => e.Email, "UQ_Users_Email").IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("datetime");
            entity.Property(e => e.PasswordHash).HasMaxLength(255);

            // 1. Mối quan hệ: Một User (Ứng viên) có nhiều Cvs
            entity.HasMany(u => u.Cvs)
                  .WithOne(cv => cv.User)
                  .HasForeignKey(cv => cv.UserId)
                  .HasConstraintName("FK_Cvs_Users");

            // 2. Mối quan hệ: Một User (Nhà tuyển dụng) có nhiều JobPostings
            entity.HasMany(u => u.JobPostings)
                  .WithOne(jp => jp.User)
                  .HasForeignKey(jp => jp.UserId)
                  .OnDelete(DeleteBehavior.Cascade); // Khi xóa User, xóa JobPostings của họ
        });

        // === CẤU HÌNH BẢNG: JobPosting ===
        modelBuilder.Entity<JobPosting>(entity =>
        {
            // 3. Mối quan hệ: Một JobPosting có nhiều Cvs (ứng tuyển)
            entity.HasMany(jp => jp.Cvs)
                  .WithOne(cv => cv.JobPosting)
                  .HasForeignKey(cv => cv.JobPostingId)
                  .OnDelete(DeleteBehavior.SetNull); // Khi xóa JobPosting, CV không bị xóa (chỉ set JobPostingId = null)
        });

        // === CẤU HÌNH BẢNG: Cv ===
        modelBuilder.Entity<Cv>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("cvs");
            entity.HasIndex(e => e.UserId, "FK_Cvs_Users"); // Index cho khóa ngoại UserId

            // Cấu hình các cột
            entity.Property(e => e.AnalysisResult).HasColumnType("text");
            entity.Property(e => e.ExtractedText).HasColumnType("text");
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.StoredFileName).HasMaxLength(255);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("datetime");
            entity.Property(e => e.JobDescriptionText).HasColumnType("TEXT");
            entity.Property(e => e.MatchingSkills).HasColumnType("TEXT");
            entity.Property(e => e.MissingSkills).HasColumnType("TEXT");
            entity.Property(e => e.RedFlags).HasColumnType("TEXT");
        });

        // === CẤU HÌNH BẢNG: JobPost (cho Crawler) ===
        modelBuilder.Entity<JobPost>(entity =>
        {
            entity.HasIndex(jp => jp.SourceUrl).IsUnique(); // Thêm index cho SourceUrl để kiểm tra trùng lặp nhanh hơn
        });

        // === CẤU HÌNH BẢNG: JobPostSkill (cho Crawler) ===
        modelBuilder.Entity<JobPostSkill>(entity =>
        {
            // Thiết lập khóa ngoại
            entity.HasOne(jps => jps.JobPost)
                  .WithMany(jp => jp.Skills)
                  .HasForeignKey(jps => jps.JobPostId)
                  .OnDelete(DeleteBehavior.Cascade); // Khi xóa JobPost, xóa các Skill liên quan
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}