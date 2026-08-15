using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenWorkoutPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_set_entries_workout_exercises_WorkoutExerciseId",
                table: "set_entries");

            migrationBuilder.DropIndex(
                name: "IX_workout_exercises_WorkoutSessionId_Order",
                table: "workout_exercises");

            migrationBuilder.DropIndex(
                name: "IX_set_entries_WorkoutExerciseId_Order",
                table: "set_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_set_entries_measurement",
                table: "set_entries");

            migrationBuilder.AddColumn<int>(
                name: "TrackingMode",
                table: "set_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE set_entries AS sets
                SET "TrackingMode" = exercises."TrackingMode"
                FROM workout_exercises AS exercises
                WHERE sets."WorkoutExerciseId" = exercises."Id";
                """);

            migrationBuilder.AlterColumn<int>(
                name: "TrackingMode",
                table: "set_entries",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveOrder",
                table: "workout_exercises",
                type: "integer",
                nullable: true,
                computedColumnSql: "CASE WHEN \"DeletedAt\" IS NULL THEN \"Order\" ELSE NULL END",
                stored: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveOrder",
                table: "set_entries",
                type: "integer",
                nullable: true,
                computedColumnSql: "CASE WHEN \"DeletedAt\" IS NULL THEN \"Order\" ELSE NULL END",
                stored: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_workout_exercises_Id_TrackingMode",
                table: "workout_exercises",
                columns: new[] { "Id", "TrackingMode" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_workout_sessions_lifecycle",
                table: "workout_sessions",
                sql: "(\"Status\" = 3 AND \"CompletedAt\" IS NOT NULL) OR (\"Status\" IN (1, 2) AND \"CompletedAt\" IS NULL)");

            migrationBuilder.Sql(
                """
                ALTER TABLE workout_exercises
                ADD CONSTRAINT "UQ_workout_exercises_active_order"
                UNIQUE ("WorkoutSessionId", "ActiveOrder")
                DEFERRABLE INITIALLY DEFERRED;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_workout_exercises_order",
                table: "workout_exercises",
                sql: "\"Order\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_set_entries_WorkoutExerciseId_TrackingMode",
                table: "set_entries",
                columns: new[] { "WorkoutExerciseId", "TrackingMode" });

            migrationBuilder.Sql(
                """
                ALTER TABLE set_entries
                ADD CONSTRAINT "UQ_set_entries_active_order"
                UNIQUE ("WorkoutExerciseId", "ActiveOrder")
                DEFERRABLE INITIALLY DEFERRED;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_set_entries_mode_measurement",
                table: "set_entries",
                sql: "(\"TrackingMode\" = 1 AND \"WeightKg\" > 0 AND \"AssistedKg\" IS NULL) OR (\"TrackingMode\" = 2 AND \"WeightKg\" IS NULL AND \"AssistedKg\" IS NULL) OR (\"TrackingMode\" = 3 AND \"WeightKg\" IS NULL AND \"AssistedKg\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_set_entries_order",
                table: "set_entries",
                sql: "\"Order\" >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_set_entries_workout_exercises_WorkoutExerciseId_TrackingMode",
                table: "set_entries",
                columns: new[] { "WorkoutExerciseId", "TrackingMode" },
                principalTable: "workout_exercises",
                principalColumns: new[] { "Id", "TrackingMode" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_set_entries_workout_exercises_WorkoutExerciseId_TrackingMode",
                table: "set_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_workout_sessions_lifecycle",
                table: "workout_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workout_exercises_Id_TrackingMode",
                table: "workout_exercises");

            migrationBuilder.Sql(
                "ALTER TABLE workout_exercises DROP CONSTRAINT \"UQ_workout_exercises_active_order\";");

            migrationBuilder.DropCheckConstraint(
                name: "CK_workout_exercises_order",
                table: "workout_exercises");

            migrationBuilder.DropIndex(
                name: "IX_set_entries_WorkoutExerciseId_TrackingMode",
                table: "set_entries");

            migrationBuilder.Sql(
                "ALTER TABLE set_entries DROP CONSTRAINT \"UQ_set_entries_active_order\";");

            migrationBuilder.DropCheckConstraint(
                name: "CK_set_entries_mode_measurement",
                table: "set_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_set_entries_order",
                table: "set_entries");

            migrationBuilder.DropColumn(
                name: "ActiveOrder",
                table: "workout_exercises");

            migrationBuilder.DropColumn(
                name: "ActiveOrder",
                table: "set_entries");

            migrationBuilder.DropColumn(
                name: "TrackingMode",
                table: "set_entries");

            migrationBuilder.CreateIndex(
                name: "IX_workout_exercises_WorkoutSessionId_Order",
                table: "workout_exercises",
                columns: new[] { "WorkoutSessionId", "Order" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_set_entries_WorkoutExerciseId_Order",
                table: "set_entries",
                columns: new[] { "WorkoutExerciseId", "Order" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_set_entries_measurement",
                table: "set_entries",
                sql: "(\"WeightKg\" > 0 AND \"AssistedKg\" IS NULL) OR (\"WeightKg\" IS NULL AND (\"AssistedKg\" IS NULL OR \"AssistedKg\" > 0))");

            migrationBuilder.AddForeignKey(
                name: "FK_set_entries_workout_exercises_WorkoutExerciseId",
                table: "set_entries",
                column: "WorkoutExerciseId",
                principalTable: "workout_exercises",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
