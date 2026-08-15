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
                    ConcurrencyToken = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_image_upload_tickets", x => x.Id);
                });

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
        }
    }
}
