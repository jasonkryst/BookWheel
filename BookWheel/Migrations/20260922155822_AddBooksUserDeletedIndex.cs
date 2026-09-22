using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddBooksUserDeletedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_books_UserId_DeletedAtUtc",
                table: "books",
                columns: new[] { "UserId", "DeletedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_books_UserId_DeletedAtUtc",
                table: "books");
        }
    }
}
