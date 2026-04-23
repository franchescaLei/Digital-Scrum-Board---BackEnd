using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalScrumBoard1.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardOrderAndRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoardOrder",
                table: "WorkItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "WorkItems",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardOrder",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "WorkItems");
        }
    }
}
