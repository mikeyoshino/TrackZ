using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGamification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "level_thresholds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    RequiredXp = table.Column<int>(type: "integer", nullable: false),
                    RulesVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_level_thresholds", x => x.Id);
                    table.CheckConstraint("CK_level_thresholds_level", "\"Level\" >= 1");
                    table.CheckConstraint("CK_level_thresholds_required_xp", "\"RequiredXp\" >= 0");
                    table.CheckConstraint("CK_level_thresholds_rules_version", "\"RulesVersion\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "user_progress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalXp = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    RulesVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_progress", x => x.Id);
                    table.CheckConstraint("CK_user_progress_level", "\"Level\" >= 1");
                    table.CheckConstraint("CK_user_progress_rules_version", "\"RulesVersion\" >= 1");
                    table.CheckConstraint("CK_user_progress_total_xp", "\"TotalXp\" >= 0");
                    table.ForeignKey(
                        name: "FK_user_progress_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "xp_ledger_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_xp_ledger_entries", x => x.Id);
                    table.CheckConstraint("CK_xp_ledger_entries_amount", "\"Amount\" <> 0 AND (\"Reason\" = 4 OR \"Amount\" > 0)");
                    table.CheckConstraint("CK_xp_ledger_entries_reason", "\"Reason\" IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_xp_ledger_entries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "level_thresholds",
                columns: new[] { "Id", "Level", "RequiredXp", "RulesVersion" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-0000-0000-000000000001"), 1, 0, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000002"), 2, 250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000003"), 3, 750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000004"), 4, 1500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000005"), 5, 2500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000006"), 6, 3750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000007"), 7, 5250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000008"), 8, 7000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000009"), 9, 9000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000010"), 10, 11250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000011"), 11, 13750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000012"), 12, 16500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000013"), 13, 19500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000014"), 14, 22750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000015"), 15, 26250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000016"), 16, 30000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000017"), 17, 34000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000018"), 18, 38250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000019"), 19, 42750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000020"), 20, 47500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000021"), 21, 52500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000022"), 22, 57750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000023"), 23, 63250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000024"), 24, 69000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000025"), 25, 75000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000026"), 26, 81250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000027"), 27, 87750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000028"), 28, 94500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000029"), 29, 101500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000030"), 30, 108750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000031"), 31, 116250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000032"), 32, 124000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000033"), 33, 132000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000034"), 34, 140250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000035"), 35, 148750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000036"), 36, 157500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000037"), 37, 166500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000038"), 38, 175750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000039"), 39, 185250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000040"), 40, 195000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000041"), 41, 205000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000042"), 42, 215250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000043"), 43, 225750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000044"), 44, 236500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000045"), 45, 247500, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000046"), 46, 258750, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000047"), 47, 270250, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000048"), 48, 282000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000049"), 49, 294000, 1 },
                    { new Guid("40000000-0000-0000-0000-000000000050"), 50, 306250, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_level_thresholds_RulesVersion_Level",
                table: "level_thresholds",
                columns: new[] { "RulesVersion", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_level_thresholds_RulesVersion_RequiredXp",
                table: "level_thresholds",
                columns: new[] { "RulesVersion", "RequiredXp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_progress_UserId",
                table: "user_progress",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_xp_ledger_entries_UserId_OriginId_CreatedAt",
                table: "xp_ledger_entries",
                columns: new[] { "UserId", "OriginId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_xp_ledger_entries_UserId_Reason_SourceId",
                table: "xp_ledger_entries",
                columns: new[] { "UserId", "Reason", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "level_thresholds");

            migrationBuilder.DropTable(
                name: "user_progress");

            migrationBuilder.DropTable(
                name: "xp_ledger_entries");
        }
    }
}
