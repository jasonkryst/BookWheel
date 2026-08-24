using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddBookTypeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookTypeId",
                table: "books",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "book_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_types", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "book_types",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Physical" },
                    { 2, "Digital" },
                    { 3, "Nook Only" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_books_BookTypeId",
                table: "books",
                column: "BookTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_book_types_Name",
                table: "book_types",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "book_types");

            migrationBuilder.DropIndex(
                name: "IX_books_BookTypeId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "BookTypeId",
                table: "books");
        }
    }
}
