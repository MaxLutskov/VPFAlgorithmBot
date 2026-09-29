using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VPFAlgorithmBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAlgorithmSchema : Migration
    {
        private static string AsExec(string sql) => $"EXEC(N'{sql.Replace("'", "''")}')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vpfalgo");

            migrationBuilder.CreateTable(
                name: "AlgorithmRules",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObjectId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MatchPattern = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlgorithmRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chats",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: false),
                    SenderTelegramId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Incidents",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatId = table.Column<int>(type: "int", nullable: false),
                    ObjectId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    AlgorithmRuleId = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartMessageId = table.Column<long>(type: "bigint", nullable: false),
                    EndMessageId = table.Column<long>(type: "bigint", nullable: true),
                    Quality = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IncomingMessages",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatId = table.Column<int>(type: "int", nullable: false),
                    TelegramMessageId = table.Column<int>(type: "int", nullable: false),
                    SenderTelegramId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramDateUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParseError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IncidentId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Objects",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Objects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResponseTemplates",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObjectId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponseTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RouteRules",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatId = table.Column<int>(type: "int", nullable: true),
                    ObjectId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "vpfalgo",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TemplateVersions",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SavedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TelegramId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Responses",
                schema: "vpfalgo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IncidentId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: true),
                    TemplateVersion = table.Column<int>(type: "int", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActionKey = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Responses_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalSchema: "vpfalgo",
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserScopes",
                schema: "vpfalgo",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ObjectId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserScopes", x => new { x.UserId, x.ObjectId });
                    table.ForeignKey(
                        name: "FK_UserScopes_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "vpfalgo",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                schema: "vpfalgo",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chats_TelegramChatId",
                schema: "vpfalgo",
                table: "Chats",
                column: "TelegramChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ChatId_AlgorithmRuleId_EndedAtUtc",
                schema: "vpfalgo",
                table: "Incidents",
                columns: new[] { "ChatId", "AlgorithmRuleId", "EndedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_StartMessageId",
                schema: "vpfalgo",
                table: "Incidents",
                column: "StartMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingMessages_ChatId_TelegramMessageId",
                schema: "vpfalgo",
                table: "IncomingMessages",
                columns: new[] { "ChatId", "TelegramMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_IncidentId_UserId_Kind",
                schema: "vpfalgo",
                table: "NotificationOutbox",
                columns: new[] { "IncidentId", "UserId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Objects_Code",
                schema: "vpfalgo",
                table: "Objects",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Responses_ActionKey",
                schema: "vpfalgo",
                table: "Responses",
                column: "ActionKey",
                unique: true,
                filter: "[ActionKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Responses_IncidentId",
                schema: "vpfalgo",
                table: "Responses",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TelegramId",
                schema: "vpfalgo",
                table: "Users",
                column: "TelegramId",
                unique: true);

            migrationBuilder.Sql(AsExec("""
                CREATE VIEW [vpfalgo].[PowerBiIncidents] AS
                SELECT i.[Id], i.[ChatId], c.[Name] AS [ChatName],
                       i.[ObjectId], o.[Code] AS [ObjectCode], o.[Name] AS [ObjectName],
                       i.[CategoryId], p.[Name] AS [CategoryName],
                       i.[AlgorithmRuleId], a.[Name] AS [AlgorithmName],
                       i.[StartedAtUtc], i.[EndedAtUtc], i.[Quality],
                       CASE WHEN i.[EndedAtUtc] IS NULL THEN 'Active' ELSE 'Resolved' END AS [ProblemState],
                       CASE WHEN r.[FirstResponseAtUtc] IS NULL THEN 'Unanswered' ELSE 'Answered' END AS [AnswerState],
                       r.[FirstResponseAtUtc], r.[ResponseCount],
                       CASE WHEN i.[EndedAtUtc] IS NOT NULL THEN DATEDIFF_BIG(SECOND,i.[StartedAtUtc],i.[EndedAtUtc]) END AS [DurationSeconds],
                       CASE WHEN r.[FirstResponseAtUtc] IS NOT NULL THEN DATEDIFF_BIG(SECOND,i.[StartedAtUtc],r.[FirstResponseAtUtc]) END AS [ResponseSeconds]
                FROM [vpfalgo].[Incidents] AS i
                LEFT JOIN [vpfalgo].[Chats] AS c ON c.[Id]=i.[ChatId]
                LEFT JOIN [vpfalgo].[Objects] AS o ON o.[Id]=i.[ObjectId]
                LEFT JOIN [vpfalgo].[Categories] AS p ON p.[Id]=i.[CategoryId]
                LEFT JOIN [vpfalgo].[AlgorithmRules] AS a ON a.[Id]=i.[AlgorithmRuleId]
                OUTER APPLY (SELECT MIN(x.[CreatedAtUtc]) AS [FirstResponseAtUtc], COUNT_BIG(*) AS [ResponseCount]
                             FROM [vpfalgo].[Responses] AS x WHERE x.[IncidentId]=i.[Id]) AS r
                """));
            migrationBuilder.Sql(AsExec("""
                CREATE VIEW [vpfalgo].[PowerBiResponses] AS
                SELECT r.[Id],r.[IncidentId],r.[CreatedAtUtc],r.[Text],r.[TemplateId],r.[TemplateVersion],
                       u.[DisplayName] AS [EmployeeName]
                FROM [vpfalgo].[Responses] AS r JOIN [vpfalgo].[Users] AS u ON u.[Id]=r.[UserId]
                """));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS [vpfalgo].[PowerBiResponses]");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [vpfalgo].[PowerBiIncidents]");
            migrationBuilder.DropTable(
                name: "AlgorithmRules",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "Categories",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "Chats",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "IncomingMessages",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "NotificationOutbox",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "Objects",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "Responses",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "ResponseTemplates",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "RouteRules",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "TemplateVersions",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "UserScopes",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "Incidents",
                schema: "vpfalgo");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "vpfalgo");
        }
    }
}
