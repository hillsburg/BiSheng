using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiSheng.Latte.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncConflictActionsAndPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocalAction",
                table: "SyncConflicts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LocalPayload",
                table: "SyncConflicts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteAction",
                table: "SyncConflicts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemotePayload",
                table: "SyncConflicts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocalAction",
                table: "SyncConflicts");

            migrationBuilder.DropColumn(
                name: "LocalPayload",
                table: "SyncConflicts");

            migrationBuilder.DropColumn(
                name: "RemoteAction",
                table: "SyncConflicts");

            migrationBuilder.DropColumn(
                name: "RemotePayload",
                table: "SyncConflicts");
        }
    }
}
