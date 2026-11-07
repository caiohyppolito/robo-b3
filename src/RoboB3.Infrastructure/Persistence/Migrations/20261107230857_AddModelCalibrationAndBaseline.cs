using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModelCalibrationAndBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Baseline",
                schema: "robo",
                table: "MetaModels",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Calibration",
                schema: "robo",
                table: "MetaModels",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Baseline",
                schema: "robo",
                table: "MetaModels");

            migrationBuilder.DropColumn(
                name: "Calibration",
                schema: "robo",
                table: "MetaModels");
        }
    }
}
