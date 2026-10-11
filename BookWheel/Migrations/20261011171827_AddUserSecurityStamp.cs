using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookWheel.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Give every existing account a unique 256-bit-length (64 hex chars) stamp.
            migrationBuilder.Sql(
                "UPDATE users SET \"SecurityStamp\" = replace(gen_random_uuid()::text || gen_random_uuid()::text, '-', '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "users");
        }
    }
}
