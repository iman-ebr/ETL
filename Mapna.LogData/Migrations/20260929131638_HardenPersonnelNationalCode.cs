using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mapna.LogData.Migrations
{
    /// <inheritdoc />
    public partial class HardenPersonnelNationalCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail loudly instead of truncating or silently picking a winner. The old receiver only *warned* on
            // national-code collisions, so production may already contain duplicates. They must be resolved by a
            // person before this migration can run. The error message lists the offending codes.
            migrationBuilder.Sql("""
                DECLARE @tooLong INT = (SELECT COUNT(*) FROM dbo.Personnel WHERE LEN(NationalCode) > 10);
                IF @tooLong > 0
                BEGIN
                    DECLARE @m1 NVARCHAR(2048) = CONCAT(N'HardenPersonnelNationalCode aborted: ', @tooLong,
                        N' row(s) have NationalCode longer than 10 characters. Fix them first.');
                    THROW 50010, @m1, 1;
                END;

                DECLARE @dups NVARCHAR(2000) = (
                    SELECT STRING_AGG(CAST(NationalCode AS NVARCHAR(MAX)), N', ')
                    FROM (SELECT TOP 50 NationalCode FROM dbo.Personnel GROUP BY NationalCode HAVING COUNT(*) > 1) d);
                IF @dups IS NOT NULL
                BEGIN
                    DECLARE @m2 NVARCHAR(2048) = CONCAT(N'HardenPersonnelNationalCode aborted: duplicate NationalCode values exist (first 50): ', @dups);
                    THROW 50011, @m2, 1;
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "NationalCode",
                table: "Personnel",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "UX_Personnel_NationalCode",
                table: "Personnel",
                column: "NationalCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Personnel_NationalCode",
                table: "Personnel");

            migrationBuilder.AlterColumn<string>(
                name: "NationalCode",
                table: "Personnel",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);
        }
    }
}
