using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations;

public partial class AddCustomExerciseNameUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_exercise_definitions_OwnerId_NormalizedName",
            table: "exercise_definitions",
            columns: new[] { "OwnerId", "NormalizedName" },
            unique: true,
            filter: "\"OwnerId\" IS NOT NULL AND NOT \"IsArchived\"");

        migrationBuilder.DropForeignKey(
            name: "FK_exercise_performances_exercise_definitions_ExerciseDefiniti~",
            table: "exercise_performances");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_exercise_definitions_Id_TrackingMode",
            table: "exercise_definitions");

        migrationBuilder.DropIndex(
            name: "IX_exercise_performances_ExerciseDefinitionId_TrackingMode",
            table: "exercise_performances");

        migrationBuilder.CreateIndex(
            name: "IX_exercise_performances_ExerciseDefinitionId",
            table: "exercise_performances",
            column: "ExerciseDefinitionId");

        migrationBuilder.AddForeignKey(
            name: "FK_exercise_performances_exercise_definitions_ExerciseDefinitionId",
            table: "exercise_performances",
            column: "ExerciseDefinitionId",
            principalTable: "exercise_definitions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            CREATE FUNCTION enforce_exercise_performance_tracking_mode()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            DECLARE expected_tracking_mode integer;
            BEGIN
                SELECT exercise."TrackingMode"
                INTO expected_tracking_mode
                FROM exercise_definitions exercise
                WHERE exercise."Id" = NEW."ExerciseDefinitionId"
                FOR UPDATE;

                IF NOT FOUND OR expected_tracking_mode <> NEW."TrackingMode" THEN
                    RAISE EXCEPTION 'Performance tracking mode must match its exercise.'
                        USING ERRCODE = '23514';
                END IF;

                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER trg_exercise_performances_tracking_mode
            BEFORE INSERT OR UPDATE OF "ExerciseDefinitionId", "TrackingMode"
            ON exercise_performances
            FOR EACH ROW EXECUTE FUNCTION enforce_exercise_performance_tracking_mode();

            CREATE FUNCTION prevent_exercise_tracking_mode_change_with_history()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW."TrackingMode" <> OLD."TrackingMode"
                   AND EXISTS (
                       SELECT 1
                       FROM exercise_performances performance
                       WHERE performance."ExerciseDefinitionId" = OLD."Id") THEN
                    RAISE EXCEPTION 'Tracking mode cannot change after performance history exists.'
                        USING ERRCODE = '23514';
                END IF;

                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER trg_exercise_definitions_tracking_mode_history
            BEFORE UPDATE OF "TrackingMode"
            ON exercise_definitions
            FOR EACH ROW EXECUTE FUNCTION prevent_exercise_tracking_mode_change_with_history();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER trg_exercise_definitions_tracking_mode_history ON exercise_definitions;
            DROP FUNCTION prevent_exercise_tracking_mode_change_with_history();
            DROP TRIGGER trg_exercise_performances_tracking_mode ON exercise_performances;
            DROP FUNCTION enforce_exercise_performance_tracking_mode();
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_exercise_performances_exercise_definitions_ExerciseDefinitionId",
            table: "exercise_performances");

        migrationBuilder.DropIndex(
            name: "IX_exercise_performances_ExerciseDefinitionId",
            table: "exercise_performances");

        migrationBuilder.AddUniqueConstraint(
            name: "AK_exercise_definitions_Id_TrackingMode",
            table: "exercise_definitions",
            columns: new[] { "Id", "TrackingMode" });

        migrationBuilder.CreateIndex(
            name: "IX_exercise_performances_ExerciseDefinitionId_TrackingMode",
            table: "exercise_performances",
            columns: new[] { "ExerciseDefinitionId", "TrackingMode" });

        migrationBuilder.AddForeignKey(
            name: "FK_exercise_performances_exercise_definitions_ExerciseDefiniti~",
            table: "exercise_performances",
            columns: new[] { "ExerciseDefinitionId", "TrackingMode" },
            principalTable: "exercise_definitions",
            principalColumns: new[] { "Id", "TrackingMode" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropIndex(
            name: "IX_exercise_definitions_OwnerId_NormalizedName",
            table: "exercise_definitions");
    }
}
