using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChatInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatInstructionOutbox",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventMessageId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatInstructionOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatInstructionOutbox_ChatId_Kind_EventMessageId",
                schema: "vpfalgo",
                table: "ChatInstructionOutbox",
                columns: new[] { "ChatId", "Kind", "EventMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatInstructionOutbox",
                schema: "vpfalgo");
        }
    }
}
