using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mapna.LogData.Migrations
{
    /// <inheritdoc />
    public partial class AddSendStatesAndAuditCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF scaffolds a bare ALTER COLUMN here, which SQL Server rejects ("PK_SendLogs is dependent on column
            // Id"), so the migration as generated would have failed at deployment. The PK must be dropped and
            // recreated around the type change. This rewrites the table (size-of-data): run it after the cleanup
            // job, in a maintenance window.
            migrationBuilder.DropPrimaryKey(name: "PK_SendLogs", table: "SendLogs");
            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "SendLogs",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("SqlServer:Identity", "1, 1")
                .OldAnnotation("SqlServer:Identity", "1, 1");
            migrationBuilder.AddPrimaryKey(name: "PK_SendLogs", table: "SendLogs", column: "Id");

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "SendLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RunId",
                table: "SendLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.DropPrimaryKey(name: "PK_ReceiveLogs", table: "ReceiveLogs");
            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "ReceiveLogs",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("SqlServer:Identity", "1, 1")
                .OldAnnotation("SqlServer:Identity", "1, 1");
            migrationBuilder.AddPrimaryKey(name: "PK_ReceiveLogs", table: "ReceiveLogs", column: "Id");

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "ReceiveLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SendStates",
                columns: table => new
                {
                    PerId = table.Column<int>(type: "int", nullable: false),
                    PayloadSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SendStates", x => x.PerId);
                });

            // Seed decision state from the audit log so that deploying this migration does not trigger a full
            // resend. The last 'Sent' row per PerId is, by construction, the last payload the receiver confirmed
            // with a 2xx. If SendLogs was already cleaned, this inserts nothing and the next run resends everything,
            // which is safe: the receiver compares field-by-field and answers "Duplicate" for unchanged rows.
            migrationBuilder.Sql("""
                INSERT INTO dbo.SendStates (PerId, PayloadSnapshot, LastSentAtUtc, LastStatus, LastAttemptAtUtc)
                SELECT l.PerId, l.PayloadSnapshot, l.OccurredAtUtc, N'Sent', l.OccurredAtUtc
                FROM (
                    SELECT PerId, PayloadSnapshot, OccurredAtUtc,
                           ROW_NUMBER() OVER (PARTITION BY PerId ORDER BY OccurredAtUtc DESC, Id DESC) AS rn
                    FROM dbo.SendLogs
                    WHERE Status = N'Sent' AND PayloadSnapshot IS NOT NULL
                ) AS l
                WHERE l.rn = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_CorrelationId",
                table: "SendLogs",
                column: "CorrelationId",
                unique: true,
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SendLogs_OccurredAtUtc",
                table: "SendLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiveLogs_CorrelationId",
                table: "ReceiveLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiveLogs_OccurredAtUtc",
                table: "ReceiveLogs",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back would destroy the decision state (=> full resend) and cannot narrow Id back to int
            // once values exceed int range. This must be a deliberate, hand-written script, not a casual
            // `dotnet ef database update <previous>`.
            migrationBuilder.Sql("""
                THROW 50001, N'AddSendStatesAndAuditCorrelation is not automatically reversible (would drop SendStates). Use a reviewed manual script.', 1;
                """);

            migrationBuilder.DropTable(
                name: "SendStates");

            migrationBuilder.DropIndex(
                name: "IX_SendLogs_CorrelationId",
                table: "SendLogs");

            migrationBuilder.DropIndex(
                name: "IX_SendLogs_OccurredAtUtc",
                table: "SendLogs");

            migrationBuilder.DropIndex(
                name: "IX_ReceiveLogs_CorrelationId",
                table: "ReceiveLogs");

            migrationBuilder.DropIndex(
                name: "IX_ReceiveLogs_OccurredAtUtc",
                table: "ReceiveLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "SendLogs");

            migrationBuilder.DropColumn(
                name: "RunId",
                table: "SendLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "ReceiveLogs");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "SendLogs",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("SqlServer:Identity", "1, 1")
                .OldAnnotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "ReceiveLogs",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("SqlServer:Identity", "1, 1")
                .OldAnnotation("SqlServer:Identity", "1, 1");
        }
    }
}
