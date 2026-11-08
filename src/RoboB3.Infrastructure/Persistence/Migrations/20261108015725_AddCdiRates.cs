using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCdiRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CdiRates",
                schema: "robo",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyPercent = table.Column<decimal>(type: "decimal(12,8)", precision: 12, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CdiRates", x => x.Date);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CdiRates",
                schema: "robo");
        }
    }
}
