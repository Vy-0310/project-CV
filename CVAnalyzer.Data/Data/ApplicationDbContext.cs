// File: Data/ApplicationDbContext.cs
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

    public virtual DbSet<Cv> Cvs { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public DbSet<JobPost> JobPosts { get; set; }
    public DbSet<JobPostSkill> JobPostSkills { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("utf8mb4_0900_ai_ci").HasCharSet("utf8mb4");

        modelBuilder.Entity<Cv>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("cvs");
            entity.HasIndex(e => e.UserId, "FK_Cvs_Users");
            entity.Property(e => e.AnalysisResult).HasColumnType("text");
            entity.Property(e => e.ExtractedText).HasColumnType("text");
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.StoredFileName).HasMaxLength(255);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("datetime");
            entity.Property(e => e.JobDescriptionText).HasColumnType("TEXT");
            entity.Property(e => e.MatchingSkills).HasColumnType("TEXT");
            entity.Property(e => e.MissingSkills).HasColumnType("TEXT");
            entity.HasOne(d => d.User).WithMany(p => p.Cvs).HasForeignKey(d => d.UserId).HasConstraintName("FK_Cvs_Users");
            entity.Property(e => e.RedFlags).HasColumnType("TEXT");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("users");
            entity.HasIndex(e => e.Email, "UQ_Users_Email").IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("datetime");
            entity.Property(e => e.PasswordHash).HasMaxLength(255);
        });

        

        OnModelCreatingPartial(modelBuilder);
    }
        

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}