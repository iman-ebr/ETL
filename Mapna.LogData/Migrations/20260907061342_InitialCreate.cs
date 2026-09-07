using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mapna.LogData.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Personnel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PerId = table.Column<int>(type: "int", nullable: false),
                    PerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerSurname = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerStatus = table.Column<int>(type: "int", nullable: false),
                    SexCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MobileNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerAddr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerLName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PerLSurname = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BornDate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NationalCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserPrincipalName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompanyId = table.Column<int>(type: "int", nullable: true),
                    PerContract = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastUpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Personnel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceiveLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PerId = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangedFields = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiveLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SendLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PerId = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedFields = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PayloadSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SendLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_PerId",
                table: "Personnel",
                column: "PerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiveLogs_PerId",
                table: "ReceiveLogs",
                column: "PerId");

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_PerId",
                table: "SendLogs",
                column: "PerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Personnel");

            migrationBuilder.DropTable(
                name: "ReceiveLogs");

            migrationBuilder.DropTable(
                name: "SendLogs");
        }
    }
}
