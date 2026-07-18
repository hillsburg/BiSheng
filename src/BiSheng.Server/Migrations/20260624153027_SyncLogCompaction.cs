using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BiSheng.Server.Migrations
{
    /// <inheritdoc />
    public partial class SyncLogCompaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SyncLogCompactionIntervalHours",
                table: "ServerConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "SyncLogMinEntriesForCompaction",
                table: "ServerConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "SyncLogStaleClientDays",
                table: "ServerConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.CreateTable(
                name: "ClientSyncStates",
                columns: table => new
                {
                    ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastSyncVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsStaleExcluded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSyncStates", x => x.ApiKeyId);
                    table.ForeignKey(
                        name: "FK_ClientSyncStates_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientSyncStates_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSyncMetas",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LogRetentionFloor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSyncMetas", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserSyncMetas_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientSyncStates_UserId",
                table: "ClientSyncStates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSyncStates_UserId_IsStaleExcluded",
                table: "ClientSyncStates",
                columns: new[] { "UserId", "IsStaleExcluded" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientSyncStates");

            migrationBuilder.DropTable(
                name: "UserSyncMetas");

            migrationBuilder.DropColumn(
                name: "SyncLogCompactionIntervalHours",
                table: "ServerConfigs");

            migrationBuilder.DropColumn(
                name: "SyncLogMinEntriesForCompaction",
                table: "ServerConfigs");

            migrationBuilder.DropColumn(
                name: "SyncLogStaleClientDays",
                table: "ServerConfigs");
        }
    }
}
