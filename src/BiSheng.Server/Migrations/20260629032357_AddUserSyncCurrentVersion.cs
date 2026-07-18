using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiSheng.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSyncCurrentVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CurrentVersion",
                table: "UserSyncMetas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("""
                UPDATE "UserSyncMetas"
                SET "CurrentVersion" = COALESCE((
                    SELECT MAX("Version")
                    FROM "SyncLogs"
                    WHERE "SyncLogs"."UserId" = "UserSyncMetas"."UserId"
                ), 0)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentVersion",
                table: "UserSyncMetas");
        }
    }
}
