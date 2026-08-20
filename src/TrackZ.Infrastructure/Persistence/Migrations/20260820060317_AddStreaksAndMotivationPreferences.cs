using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStreaksAndMotivationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<int>(
                name: "WeeklyWorkoutGoal",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "streak_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentWeeks = table.Column<int>(type: "integer", nullable: false),
                    BestWeeks = table.Column<int>(type: "integer", nullable: false),
                    LastEvaluatedIsoYear = table.Column<int>(type: "integer", nullable: false),
                    LastEvaluatedIsoWeek = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_streak_states", x => x.Id);
                    table.CheckConstraint("CK_streak_states_best", "\"BestWeeks\" >= \"CurrentWeeks\"");
                    table.CheckConstraint("CK_streak_states_current", "\"CurrentWeeks\" >= 0");
                    table.CheckConstraint("CK_streak_states_iso_week", "\"LastEvaluatedIsoYear\" >= 1 AND \"LastEvaluatedIsoWeek\" BETWEEN 1 AND 53");
                    table.ForeignKey(
                        name: "FK_streak_states_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_weekly_workout_goal",
                table: "users",
                sql: "\"WeeklyWorkoutGoal\" BETWEEN 1 AND 7");

            migrationBuilder.CreateIndex(
                name: "IX_streak_states_UserId",
                table: "streak_states",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "streak_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_weekly_workout_goal",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "WeeklyWorkoutGoal",
                table: "users");
        }
    }
}
