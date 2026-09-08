using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSetCoachingMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "EffortScore", table: "set_entries", type: "integer", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsWarmup", table: "set_entries", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "HasPain", table: "set_entries", type: "boolean", nullable: true);
            migrationBuilder.AddCheckConstraint(name: "CK_set_entries_effort_score", table: "set_entries",
                sql: "\"EffortScore\" IS NULL OR \"EffortScore\" BETWEEN 0 AND 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_set_entries_effort_score", table: "set_entries");
            migrationBuilder.DropColumn(name: "EffortScore", table: "set_entries");
            migrationBuilder.DropColumn(name: "IsWarmup", table: "set_entries");
            migrationBuilder.DropColumn(name: "HasPain", table: "set_entries");
        }
    }
}
