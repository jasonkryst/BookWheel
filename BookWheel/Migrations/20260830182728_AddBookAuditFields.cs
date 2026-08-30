using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "books",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill existing rows: the creator is the book's owner.
            migrationBuilder.Sql(@"UPDATE books SET ""CreatedByUserId"" = ""UserId""");

            // Drop the Guid.Empty default so future app-level inserts must always supply the value.
            migrationBuilder.Sql(@"ALTER TABLE books ALTER COLUMN ""CreatedByUserId"" DROP DEFAULT");

            migrationBuilder.AddColumn<Guid>(
                name: "LastUpdatedByUserId",
                table: "books",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                table: "books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_books_CreatedByUserId",
                table: "books",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_books_LastUpdatedByUserId",
                table: "books",
                column: "LastUpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_books_CreatedByUserId",
                table: "books");

            migrationBuilder.DropIndex(
                name: "IX_books_LastUpdatedByUserId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "LastUpdatedByUserId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "books");
        }
    }
}
