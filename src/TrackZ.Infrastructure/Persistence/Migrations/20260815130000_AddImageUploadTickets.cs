using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUploadTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_exercise_images_ExerciseDefinitionId_Version",
                table: "exercise_images");

            migrationBuilder.CreateTable(
                name: "image_upload_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StagingObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DeclaredContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeclaredLength = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ExerciseImageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessingStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessingLeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_upload_tickets", x => x.Id);
                    table.CheckConstraint("CK_image_upload_tickets_state", "\"State\" IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("CK_image_upload_tickets_processing_lease", "(\"State\" = 3 AND \"ProcessingStartedAt\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND \"ProcessingLeaseId\" IS NOT NULL) OR (\"State\" <> 3 AND \"ProcessingStartedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"ProcessingLeaseId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_image_upload_tickets_exercise_definitions_ExerciseDefinitio~",
                        column: x => x.ExerciseDefinitionId,
                        principalTable: "exercise_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_image_upload_tickets_exercise_images_ExerciseImageId",
                        column: x => x.ExerciseImageId,
                        principalTable: "exercise_images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_images_ExerciseDefinitionId_Version",
                table: "exercise_images",
                columns: new[] { "ExerciseDefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_image_upload_tickets_ExerciseDefinitionId",
                table: "image_upload_tickets",
                column: "ExerciseDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_image_upload_tickets_ExerciseImageId",
                table: "image_upload_tickets",
                column: "ExerciseImageId");

            migrationBuilder.CreateIndex(
                name: "IX_image_upload_tickets_OwnerId_Id",
                table: "image_upload_tickets",
                columns: new[] { "OwnerId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "image_upload_tickets");

            migrationBuilder.DropIndex(
                name: "IX_exercise_images_ExerciseDefinitionId_Version",
                table: "exercise_images");

            migrationBuilder.CreateIndex(
                name: "IX_exercise_images_ExerciseDefinitionId_Version",
                table: "exercise_images",
                columns: new[] { "ExerciseDefinitionId", "Version" });
        }
    }
}
