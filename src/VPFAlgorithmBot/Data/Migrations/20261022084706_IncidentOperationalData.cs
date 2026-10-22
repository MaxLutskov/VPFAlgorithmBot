using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class IncidentOperationalData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperationalData",
                schema: "vpfalgo",
                table: "Incidents",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OperationalData",
                schema: "vpfalgo",
                table: "Incidents");
        }
    }
}
