using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddBookSoftDeleteAndSpinHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAtUtc",
                table: "books",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "spin_selections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spin_selections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_spin_selections_BookId",
                table: "spin_selections",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_spin_selections_UserId",
                table: "spin_selections",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spin_selections");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "books");
        }
    }
}
