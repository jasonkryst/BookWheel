using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastLoginAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAtUtc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLoginAtUtc",
                table: "users");
        }
    }
}
