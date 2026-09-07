using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddBookInfoProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AnalyticsConsentOptedOut",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PreferredBookInfoProviderId",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BookInfoProviderId",
                table: "books",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "book_info_providers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_info_providers", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "book_info_providers",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Open Library" },
                    { 2, "Google Books" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_books_BookInfoProviderId",
                table: "books",
                column: "BookInfoProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_book_info_providers_Name",
                table: "book_info_providers",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "book_info_providers");

            migrationBuilder.DropIndex(
                name: "IX_books_BookInfoProviderId",
                table: "books");

            migrationBuilder.DropColumn(
                name: "AnalyticsConsentOptedOut",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PreferredBookInfoProviderId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "users");

            migrationBuilder.DropColumn(
                name: "BookInfoProviderId",
                table: "books");
        }
    }
}
