using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EternityServer.Migrations
{
    /// <inheritdoc />
    public partial class UpdateJobTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "BestBoardState",
                table: "Jobs",
                type: "longblob",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstWorkerId",
                table: "Jobs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "MaxDepthFound",
                table: "Jobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SecondWorkerId",
                table: "Jobs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BestBoardState",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "FirstWorkerId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MaxDepthFound",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SecondWorkerId",
                table: "Jobs");
        }
    }
}
