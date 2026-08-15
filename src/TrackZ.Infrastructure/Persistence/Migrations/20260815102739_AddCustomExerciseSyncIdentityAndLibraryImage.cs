using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomExerciseSyncIdentityAndLibraryImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientOperationId",
                table: "exercise_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryImageId",
                table: "exercise_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercise_definitions_LibraryImageId",
                table: "exercise_definitions",
                column: "LibraryImageId");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_definitions_OwnerId_ClientOperationId",
                table: "exercise_definitions",
                columns: new[] { "OwnerId", "ClientOperationId" },
                unique: true,
                filter: "\"OwnerId\" IS NOT NULL AND \"ClientOperationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_exercise_definitions_exercise_images_LibraryImageId",
                table: "exercise_definitions",
                column: "LibraryImageId",
                principalTable: "exercise_images",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_exercise_definitions_exercise_images_LibraryImageId",
                table: "exercise_definitions");

            migrationBuilder.DropIndex(
                name: "IX_exercise_definitions_LibraryImageId",
                table: "exercise_definitions");

            migrationBuilder.DropIndex(
                name: "IX_exercise_definitions_OwnerId_ClientOperationId",
                table: "exercise_definitions");

            migrationBuilder.DropColumn(
                name: "ClientOperationId",
                table: "exercise_definitions");

            migrationBuilder.DropColumn(
                name: "LibraryImageId",
                table: "exercise_definitions");
        }
    }
}
