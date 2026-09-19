using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mapna.LogData.Migrations
{
    /// <inheritdoc />
    public partial class Victus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlayLoadHash",
                table: "SendLogs",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayLoadHash",
                table: "SendLogs");
        }
    }
}
