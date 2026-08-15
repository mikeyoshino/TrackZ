using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exercise_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BodyPart = table.Column<int>(type: "integer", nullable: false),
                    TrackingMode = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    HasSetHistory = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "exercise_images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPrivate = table.Column<bool>(type: "boolean", nullable: false),
                    MasterObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ThumbnailObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RightsReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReviewState = table.Column<int>(type: "integer", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AnatomyApproved = table.Column<bool>(type: "boolean", nullable: false),
                    MovementApproved = table.Column<bool>(type: "boolean", nullable: false),
                    RightsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exercise_images_exercise_definitions_ExerciseDefinitionId",
                        column: x => x.ExerciseDefinitionId,
                        principalTable: "exercise_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exercise_performances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastPerformedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastBestWeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    LastBestAssistedKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    LastBestReps = table.Column<int>(type: "integer", nullable: true),
                    AllTimeBestWeightKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    AllTimeBestAssistedKg = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    AllTimeBestReps = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_performances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exercise_performances_exercise_definitions_ExerciseDefiniti~",
                        column: x => x.ExerciseDefinitionId,
                        principalTable: "exercise_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_definitions_Name_Id",
                table: "exercise_definitions",
                columns: new[] { "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_definitions_OwnerId_IsArchived",
                table: "exercise_definitions",
                columns: new[] { "OwnerId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_images_ExerciseDefinitionId_Version",
                table: "exercise_images",
                columns: new[] { "ExerciseDefinitionId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_performances_ExerciseDefinitionId",
                table: "exercise_performances",
                column: "ExerciseDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_performances_UserId_ExerciseDefinitionId",
                table: "exercise_performances",
                columns: new[] { "UserId", "ExerciseDefinitionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_images");

            migrationBuilder.DropTable(
                name: "exercise_performances");

            migrationBuilder.DropTable(
                name: "exercise_definitions");
        }
    }
}
