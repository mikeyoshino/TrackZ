using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "badge_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Criteria = table.Column<int>(type: "integer", nullable: false),
                    Threshold = table.Column<int>(type: "integer", nullable: false),
                    NameResourceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DescriptionResourceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IconKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CriteriaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_badge_definitions", x => x.Id);
                    table.CheckConstraint("CK_badge_definitions_criteria", "\"Criteria\" IN (1, 2, 3, 4)");
                    table.CheckConstraint("CK_badge_definitions_threshold", "\"Threshold\" >= 1");
                    table.CheckConstraint("CK_badge_definitions_version", "\"CriteriaVersion\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "badge_audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_badge_audit_events", x => x.Id);
                    table.CheckConstraint("CK_badge_audit_events_action", "\"Action\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_badge_audit_events_badge_definitions_BadgeDefinitionId",
                        column: x => x.BadgeDefinitionId,
                        principalTable: "badge_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_badge_audit_events_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_badges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EarnedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_badges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_badges_badge_definitions_BadgeDefinitionId",
                        column: x => x.BadgeDefinitionId,
                        principalTable: "badge_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_badges_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "badge_definitions",
                columns: new[] { "Id", "Criteria", "CriteriaVersion", "DescriptionResourceKey", "IconKey", "Key", "NameResourceKey", "Threshold" },
                values: new object[,]
                {
                    { new Guid("50000000-0000-0000-0000-000000000001"), 1, 1, "Badge_FirstWorkout_Description", "badge-first-workout", "first-workout", "Badge_FirstWorkout_Name", 1 },
                    { new Guid("50000000-0000-0000-0000-000000000002"), 1, 1, "Badge_Workouts10_Description", "badge-workouts-10", "workouts-10", "Badge_Workouts10_Name", 10 },
                    { new Guid("50000000-0000-0000-0000-000000000003"), 1, 1, "Badge_Workouts25_Description", "badge-workouts-25", "workouts-25", "Badge_Workouts25_Name", 25 },
                    { new Guid("50000000-0000-0000-0000-000000000004"), 1, 1, "Badge_Workouts50_Description", "badge-workouts-50", "workouts-50", "Badge_Workouts50_Name", 50 },
                    { new Guid("50000000-0000-0000-0000-000000000005"), 2, 1, "Badge_Streak4_Description", "badge-streak-4", "streak-4", "Badge_Streak4_Name", 4 },
                    { new Guid("50000000-0000-0000-0000-000000000006"), 2, 1, "Badge_Streak8_Description", "badge-streak-8", "streak-8", "Badge_Streak8_Name", 8 },
                    { new Guid("50000000-0000-0000-0000-000000000007"), 2, 1, "Badge_Streak12_Description", "badge-streak-12", "streak-12", "Badge_Streak12_Name", 12 },
                    { new Guid("50000000-0000-0000-0000-000000000008"), 3, 1, "Badge_Exercises10_Description", "badge-exercises-10", "exercises-10", "Badge_Exercises10_Name", 10 },
                    { new Guid("50000000-0000-0000-0000-000000000009"), 3, 1, "Badge_Exercises25_Description", "badge-exercises-25", "exercises-25", "Badge_Exercises25_Name", 25 },
                    { new Guid("50000000-0000-0000-0000-000000000010"), 4, 1, "Badge_FirstPr_Description", "badge-first-pr", "first-pr", "Badge_FirstPr_Name", 1 },
                    { new Guid("50000000-0000-0000-0000-000000000011"), 4, 1, "Badge_Prs10_Description", "badge-prs-10", "prs-10", "Badge_Prs10_Name", 10 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_badge_audit_events_BadgeDefinitionId",
                table: "badge_audit_events",
                column: "BadgeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_badge_audit_events_UserId_OccurredAt_Id",
                table: "badge_audit_events",
                columns: new[] { "UserId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_badge_definitions_Key",
                table: "badge_definitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_badges_BadgeDefinitionId",
                table: "user_badges",
                column: "BadgeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_user_badges_UserId_BadgeDefinitionId",
                table: "user_badges",
                columns: new[] { "UserId", "BadgeDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "badge_audit_events");

            migrationBuilder.DropTable(
                name: "user_badges");

            migrationBuilder.DropTable(
                name: "badge_definitions");
        }
    }
}
