using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlateCountLoad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_set_entries_mode_measurement",
                table: "set_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_exercise_performances_all_time_best_shape",
                table: "exercise_performances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_exercise_performances_last_best_shape",
                table: "exercise_performances");

            migrationBuilder.AddColumn<int>(
                name: "PlateCount",
                table: "set_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AllTimeBestPlateCount",
                table: "exercise_performances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastBestPlateCount",
                table: "exercise_performances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_set_entries_mode_measurement",
                table: "set_entries",
                sql: "(\"TrackingMode\" = 1 AND \"AssistedKg\" IS NULL AND ((\"WeightKg\" > 0 AND \"PlateCount\" IS NULL) OR (\"WeightKg\" IS NULL AND \"PlateCount\" BETWEEN 1 AND 999))) OR (\"TrackingMode\" = 2 AND \"WeightKg\" IS NULL AND \"AssistedKg\" IS NULL AND \"PlateCount\" IS NULL) OR (\"TrackingMode\" = 3 AND \"WeightKg\" IS NULL AND ((\"AssistedKg\" > 0 AND \"PlateCount\" IS NULL) OR (\"AssistedKg\" IS NULL AND \"PlateCount\" BETWEEN 1 AND 999)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_exercise_performances_all_time_best_shape",
                table: "exercise_performances",
                sql: "\"AllTimeBestReps\" > 0 AND ((\"TrackingMode\" = 1 AND \"AllTimeBestAssistedKg\" IS NULL AND ((\"AllTimeBestWeightKg\" > 0 AND \"AllTimeBestPlateCount\" IS NULL) OR (\"AllTimeBestWeightKg\" IS NULL AND \"AllTimeBestPlateCount\" > 0))) OR (\"TrackingMode\" = 2 AND \"AllTimeBestWeightKg\" IS NULL AND \"AllTimeBestAssistedKg\" IS NULL AND \"AllTimeBestPlateCount\" IS NULL) OR (\"TrackingMode\" = 3 AND \"AllTimeBestWeightKg\" IS NULL AND ((\"AllTimeBestAssistedKg\" > 0 AND \"AllTimeBestPlateCount\" IS NULL) OR (\"AllTimeBestAssistedKg\" IS NULL AND \"AllTimeBestPlateCount\" > 0))))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_exercise_performances_last_best_shape",
                table: "exercise_performances",
                sql: "\"LastPerformedAt\" IS NOT NULL AND \"LastBestReps\" > 0 AND ((\"TrackingMode\" = 1 AND \"LastBestAssistedKg\" IS NULL AND ((\"LastBestWeightKg\" > 0 AND \"LastBestPlateCount\" IS NULL) OR (\"LastBestWeightKg\" IS NULL AND \"LastBestPlateCount\" > 0))) OR (\"TrackingMode\" = 2 AND \"LastBestWeightKg\" IS NULL AND \"LastBestAssistedKg\" IS NULL AND \"LastBestPlateCount\" IS NULL) OR (\"TrackingMode\" = 3 AND \"LastBestWeightKg\" IS NULL AND ((\"LastBestAssistedKg\" > 0 AND \"LastBestPlateCount\" IS NULL) OR (\"LastBestAssistedKg\" IS NULL AND \"LastBestPlateCount\" > 0))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_set_entries_mode_measurement",
                table: "set_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_exercise_performances_all_time_best_shape",
                table: "exercise_performances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_exercise_performances_last_best_shape",
                table: "exercise_performances");

            migrationBuilder.DropColumn(
                name: "PlateCount",
                table: "set_entries");

            migrationBuilder.DropColumn(
                name: "AllTimeBestPlateCount",
                table: "exercise_performances");

            migrationBuilder.DropColumn(
                name: "LastBestPlateCount",
                table: "exercise_performances");

            migrationBuilder.AddCheckConstraint(
                name: "CK_set_entries_mode_measurement",
                table: "set_entries",
                sql: "(\"TrackingMode\" = 1 AND \"WeightKg\" > 0 AND \"AssistedKg\" IS NULL) OR (\"TrackingMode\" = 2 AND \"WeightKg\" IS NULL AND \"AssistedKg\" IS NULL) OR (\"TrackingMode\" = 3 AND \"WeightKg\" IS NULL AND \"AssistedKg\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_exercise_performances_all_time_best_shape",
                table: "exercise_performances",
                sql: "\"AllTimeBestReps\" IS NOT NULL AND \"AllTimeBestReps\" > 0 AND ((\"TrackingMode\" = 1 AND \"AllTimeBestWeightKg\" IS NOT NULL AND \"AllTimeBestWeightKg\" > 0 AND \"AllTimeBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 2 AND \"AllTimeBestWeightKg\" IS NULL AND \"AllTimeBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 3 AND \"AllTimeBestWeightKg\" IS NULL AND \"AllTimeBestAssistedKg\" IS NOT NULL AND \"AllTimeBestAssistedKg\" > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_exercise_performances_last_best_shape",
                table: "exercise_performances",
                sql: "\"LastPerformedAt\" IS NOT NULL AND \"LastBestReps\" IS NOT NULL AND \"LastBestReps\" > 0 AND ((\"TrackingMode\" = 1 AND \"LastBestWeightKg\" IS NOT NULL AND \"LastBestWeightKg\" > 0 AND \"LastBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 2 AND \"LastBestWeightKg\" IS NULL AND \"LastBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 3 AND \"LastBestWeightKg\" IS NULL AND \"LastBestAssistedKg\" IS NOT NULL AND \"LastBestAssistedKg\" > 0))");
        }
    }
}
