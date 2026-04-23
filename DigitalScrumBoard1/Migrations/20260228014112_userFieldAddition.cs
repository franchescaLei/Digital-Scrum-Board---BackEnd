using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalScrumBoard1.Migrations
{
    /// <inheritdoc />
    public partial class userFieldAddition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkItems_Priority",
                table: "WorkItems",
                sql: "[Priority] IS NULL OR [Priority] IN ('Low','Medium','High','Critical')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkItems_Status",
                table: "WorkItems",
                sql: "[Status] IN ('To-do','Ongoing','For Checking','Completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkItems_Priority",
                table: "WorkItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkItems_Status",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");
        }
    }
}
