using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshDeviceIdentityV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "refresh_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "LEGACY");

            // Legacy tokens lack a trustworthy device binding; force reauthentication rather than leave active unusable sessions.
            migrationBuilder.Sql("UPDATE refresh_tokens SET \"RevokedAt\" = NOW() WHERE \"RevokedAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "refresh_tokens");
        }
    }
}
