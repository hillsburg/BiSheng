using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bisheng.Latte.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalNoteRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoteRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SyncedToServer = table.Column<bool>(type: "INTEGER", nullable: false),
                    ServerRevisionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteRevisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteRevisions_NoteId_CreatedAt",
                table: "NoteRevisions",
                columns: new[] { "NoteId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NoteRevisions_NoteId_RevisionNumber",
                table: "NoteRevisions",
                columns: new[] { "NoteId", "RevisionNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoteRevisions");
        }
    }
}
