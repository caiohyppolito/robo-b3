using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BrokerTicket",
                schema: "robo",
                table: "PaperPositions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                schema: "robo",
                table: "PaperPositions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Paper");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                schema: "robo",
                table: "PaperFills",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Paper");

            migrationBuilder.CreateTable(
                name: "ExecutionSettings",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperPositions_Mode",
                schema: "robo",
                table: "PaperPositions",
                column: "Mode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionSettings",
                schema: "robo");

            migrationBuilder.DropIndex(
                name: "IX_PaperPositions_Mode",
                schema: "robo",
                table: "PaperPositions");

            migrationBuilder.DropColumn(
                name: "BrokerTicket",
                schema: "robo",
                table: "PaperPositions");

            migrationBuilder.DropColumn(
                name: "Mode",
                schema: "robo",
                table: "PaperPositions");

            migrationBuilder.DropColumn(
                name: "Mode",
                schema: "robo",
                table: "PaperFills");
        }
    }
}
