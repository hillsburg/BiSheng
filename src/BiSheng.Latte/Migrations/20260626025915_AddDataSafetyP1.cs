using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bisheng.Latte.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSafetyP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Notes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Folders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EditJournal",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditJournal", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_DeletedAt",
                table: "Notes",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_DeletedAt",
                table: "Folders",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EditJournal_CreatedAtUtc",
                table: "EditJournal",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EditJournal_EntityType_EntityId_CreatedAtUtc",
                table: "EditJournal",
                columns: new[] { "EntityType", "EntityId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EditJournal");

            migrationBuilder.DropIndex(
                name: "IX_Notes_DeletedAt",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Folders_DeletedAt",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Folders");
        }
    }
}
