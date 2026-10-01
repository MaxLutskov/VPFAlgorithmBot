using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class GroupChatReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChatTelegramId",
                schema: "vpfalgo",
                table: "PendingCustomAnswers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromptMessageTelegramId",
                schema: "vpfalgo",
                table: "PendingCustomAnswers",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChatTelegramId",
                schema: "vpfalgo",
                table: "PendingCustomAnswers");

            migrationBuilder.DropColumn(
                name: "PromptMessageTelegramId",
                schema: "vpfalgo",
                table: "PendingCustomAnswers");
        }
    }
}
