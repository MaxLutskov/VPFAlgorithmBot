using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlgorithmSpecificTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlgorithmRuleId",
                schema: "vpfalgo",
                table: "ResponseTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE t
                SET [AlgorithmRuleId] = candidate.[AlgorithmRuleId]
                FROM [vpfalgo].[ResponseTemplates] AS t
                CROSS APPLY (
                    SELECT MIN(a.[Id]) AS [AlgorithmRuleId], COUNT(*) AS [Matches]
                    FROM [vpfalgo].[AlgorithmRules] AS a
                    WHERE (t.[ObjectId] IS NULL OR a.[ObjectId] = t.[ObjectId])
                      AND (t.[CategoryId] IS NULL OR a.[CategoryId] = t.[CategoryId])
                ) AS candidate
                WHERE candidate.[Matches] = 1;

                UPDATE [vpfalgo].[ResponseTemplates]
                SET [Enabled] = 0
                WHERE [AlgorithmRuleId] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlgorithmRuleId",
                schema: "vpfalgo",
                table: "ResponseTemplates");
        }
    }
}
