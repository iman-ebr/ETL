using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mapna.LogData.Migrations
{
    /// <inheritdoc />
    public partial class updateTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SendLogs_PerId",
                table: "SendLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_PerId_Status_OccurredAtUtc",
                table: "SendLogs",
                columns: new[] { "PerId", "Status", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SendLogs_PerId_Status_OccurredAtUtc",
                table: "SendLogs");

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_PerId",
                table: "SendLogs",
                column: "PerId");
        }
    }
}
