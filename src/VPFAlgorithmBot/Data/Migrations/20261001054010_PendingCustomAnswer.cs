using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class PendingCustomAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingCustomAnswers",
                schema: "vpfalgo",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    IncidentId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingCustomAnswers", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingCustomAnswers",
                schema: "vpfalgo");
        }
    }
}
