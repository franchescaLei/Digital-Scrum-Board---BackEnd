using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigitalScrumBoard1.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserID",
                table: "Notifications");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CreatedAt",
                table: "WorkItems",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_SprintID_BoardOrder",
                table: "WorkItems",
                columns: new[] { "SprintID", "BoardOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_SprintID_Status",
                table: "WorkItems",
                columns: new[] { "SprintID", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_Status",
                table: "WorkItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemHistories_ChangedAt",
                table: "WorkItemHistories",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemHistories_WorkItemID_ChangedAt",
                table: "WorkItemHistories",
                columns: new[] { "WorkItemID", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemComments_WorkItemID_CreatedAt",
                table: "WorkItemComments",
                columns: new[] { "WorkItemID", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_Status",
                table: "Sprints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserID_IsRead",
                table: "Notifications",
                columns: new[] { "UserID", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TargetType",
                table: "AuditLogs",
                column: "TargetType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserID_Timestamp",
                table: "AuditLogs",
                columns: new[] { "UserID", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItems_CreatedAt",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_SprintID_BoardOrder",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_SprintID_Status",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_Status",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItemHistories_ChangedAt",
                table: "WorkItemHistories");

            migrationBuilder.DropIndex(
                name: "IX_WorkItemHistories_WorkItemID_ChangedAt",
                table: "WorkItemHistories");

            migrationBuilder.DropIndex(
                name: "IX_WorkItemComments_WorkItemID_CreatedAt",
                table: "WorkItemComments");

            migrationBuilder.DropIndex(
                name: "IX_Sprints_Status",
                table: "Sprints");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserID_IsRead",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TargetType",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserID_Timestamp",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserID",
                table: "Notifications",
                column: "UserID");
        }
    }
}
