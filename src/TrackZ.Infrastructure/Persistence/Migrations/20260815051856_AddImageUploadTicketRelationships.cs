using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUploadTicketRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_image_upload_tickets_ExerciseDefinitionId",
                table: "image_upload_tickets",
                column: "ExerciseDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_image_upload_tickets_ExerciseImageId",
                table: "image_upload_tickets",
                column: "ExerciseImageId");

            migrationBuilder.AddForeignKey(
                name: "FK_image_upload_tickets_exercise_definitions_ExerciseDefinitio~",
                table: "image_upload_tickets",
                column: "ExerciseDefinitionId",
                principalTable: "exercise_definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_image_upload_tickets_exercise_images_ExerciseImageId",
                table: "image_upload_tickets",
                column: "ExerciseImageId",
                principalTable: "exercise_images",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_image_upload_tickets_exercise_definitions_ExerciseDefinitio~",
                table: "image_upload_tickets");

            migrationBuilder.DropForeignKey(
                name: "FK_image_upload_tickets_exercise_images_ExerciseImageId",
                table: "image_upload_tickets");

            migrationBuilder.DropIndex(
                name: "IX_image_upload_tickets_ExerciseDefinitionId",
                table: "image_upload_tickets");

            migrationBuilder.DropIndex(
                name: "IX_image_upload_tickets_ExerciseImageId",
                table: "image_upload_tickets");
        }
    }
}
