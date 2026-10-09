using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class DailyDigests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyDigestDeliveries",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    LocalDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Part = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(3900)", maxLength: 3900, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyDigestDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyDigestSchedules",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatId = table.Column<int>(type: "int", nullable: false),
                    LocalTime = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    ObjectIdsCsv = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyDigestSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyDigestDeliveries_ScheduleId_LocalDate_Part",
                schema: "vpfalgo",
                table: "DailyDigestDeliveries",
                columns: new[] { "ScheduleId", "LocalDate", "Part" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyDigestSchedules_ChatId",
                schema: "vpfalgo",
                table: "DailyDigestSchedules",
                column: "ChatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyDigestDeliveries",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "DailyDigestSchedules",
                schema: "vpfalgo");
        }
    }
}
