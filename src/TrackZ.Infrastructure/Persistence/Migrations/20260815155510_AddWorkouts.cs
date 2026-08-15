using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workout_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workout_sessions", x => x.Id);
                    table.CheckConstraint("CK_workout_sessions_status", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_workout_sessions_users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workout_exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingMode = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workout_exercises", x => x.Id);
                    table.CheckConstraint("CK_workout_exercises_tracking_mode", "\"TrackingMode\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_workout_exercises_exercise_definitions_ExerciseDefinitionId",
                        column: x => x.ExerciseDefinitionId,
                        principalTable: "exercise_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workout_exercises_workout_sessions_WorkoutSessionId",
                        column: x => x.WorkoutSessionId,
                        principalTable: "workout_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "set_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                    AssistedKg = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                    Reps = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_set_entries", x => x.Id);
                    table.CheckConstraint("CK_set_entries_measurement", "(\"WeightKg\" > 0 AND \"AssistedKg\" IS NULL) OR (\"WeightKg\" IS NULL AND (\"AssistedKg\" IS NULL OR \"AssistedKg\" > 0))");
                    table.CheckConstraint("CK_set_entries_reps", "\"Reps\" BETWEEN 1 AND 999");
                    table.ForeignKey(
                        name: "FK_set_entries_workout_exercises_WorkoutExerciseId",
                        column: x => x.WorkoutExerciseId,
                        principalTable: "workout_exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_set_entries_WorkoutExerciseId_Order",
                table: "set_entries",
                columns: new[] { "WorkoutExerciseId", "Order" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_workout_exercises_ExerciseDefinitionId_WorkoutSessionId",
                table: "workout_exercises",
                columns: new[] { "ExerciseDefinitionId", "WorkoutSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_workout_exercises_WorkoutSessionId_Order",
                table: "workout_exercises",
                columns: new[] { "WorkoutSessionId", "Order" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_workout_sessions_OwnerId_CompletedAt_Id",
                table: "workout_sessions",
                columns: new[] { "OwnerId", "CompletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_workout_sessions_OwnerId_Status_DeletedAt",
                table: "workout_sessions",
                columns: new[] { "OwnerId", "Status", "DeletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "set_entries");

            migrationBuilder.DropTable(
                name: "workout_exercises");

            migrationBuilder.DropTable(
                name: "workout_sessions");
        }
    }
}
