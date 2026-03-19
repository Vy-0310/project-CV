using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CVAnalyzer.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCategoryToCv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JobCategory",
                table: "cvs",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_0900_ai_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JobCategory",
                table: "cvs");
        }
    }
}
