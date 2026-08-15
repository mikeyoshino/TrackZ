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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_exercise_definitions_OwnerId_NormalizedName",
            table: "exercise_definitions");
    }
}
