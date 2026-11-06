using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bisheng.Server.Migrations
{
    /// <inheritdoc />
    public partial class HardenAdminAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AuthLockedUntilUtc",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AuthVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<int>(
                name: "FailedAuthAttempts",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "LastTotpTimeStep",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthLockedUntilUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AuthVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FailedAuthAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastTotpTimeStep",
                table: "Users");
        }
    }
}
