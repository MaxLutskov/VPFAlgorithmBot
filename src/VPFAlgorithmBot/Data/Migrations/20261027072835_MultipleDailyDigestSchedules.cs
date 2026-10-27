using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class MultipleDailyDigestSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyDigestSchedules_ChatId",
                schema: "vpfalgo",
                table: "DailyDigestSchedules");

            migrationBuilder.CreateIndex(
                name: "IX_DailyDigestSchedules_ChatId",
                schema: "vpfalgo",
                table: "DailyDigestSchedules",
                column: "ChatId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyDigestSchedules_ChatId",
                schema: "vpfalgo",
                table: "DailyDigestSchedules");

            migrationBuilder.CreateIndex(
                name: "IX_DailyDigestSchedules_ChatId",
                schema: "vpfalgo",
                table: "DailyDigestSchedules",
                column: "ChatId",
                unique: true);
        }
    }
}
