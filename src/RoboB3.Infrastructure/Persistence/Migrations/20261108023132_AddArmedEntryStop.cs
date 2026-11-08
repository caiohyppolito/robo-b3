using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArmedEntryStop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArmedDirection",
                schema: "robo",
                table: "StrategyAssignments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmedLevel",
                schema: "robo",
                table: "StrategyAssignments",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmedStop",
                schema: "robo",
                table: "StrategyAssignments",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArmedTarget",
                schema: "robo",
                table: "StrategyAssignments",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArmedUntil",
                schema: "robo",
                table: "StrategyAssignments",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArmedDirection",
                schema: "robo",
                table: "StrategyAssignments");

            migrationBuilder.DropColumn(
                name: "ArmedLevel",
                schema: "robo",
                table: "StrategyAssignments");

            migrationBuilder.DropColumn(
                name: "ArmedStop",
                schema: "robo",
                table: "StrategyAssignments");

            migrationBuilder.DropColumn(
                name: "ArmedTarget",
                schema: "robo",
                table: "StrategyAssignments");

            migrationBuilder.DropColumn(
                name: "ArmedUntil",
                schema: "robo",
                table: "StrategyAssignments");
        }
    }
}
