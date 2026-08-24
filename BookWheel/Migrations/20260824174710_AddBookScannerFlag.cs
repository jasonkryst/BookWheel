using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddBookScannerFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AddedByScanner",
                table: "books",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedByScanner",
                table: "books");
        }
    }
}
