using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTargetsMetricsAndExperiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TargetPrice",
                schema: "robo",
                table: "PaperPositions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Metrics_AmbiguousTrades",
                schema: "robo",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Metrics_CVaR95Percent",
                schema: "robo",
                table: "BacktestRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Metrics_Calmar",
                schema: "robo",
                table: "BacktestRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Metrics_ExpectancyMargin95",
                schema: "robo",
                table: "BacktestRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Metrics_ExpectancyPerTrade",
                schema: "robo",
                table: "BacktestRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Metrics_Sortino",
                schema: "robo",
                table: "BacktestRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Metrics_TurnoverPerYear",
                schema: "robo",
                table: "BacktestRuns",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ResearchExperiments",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Trials = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CodeVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchExperiments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchExperiments_CreatedAt",
                schema: "robo",
                table: "ResearchExperiments",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResearchExperiments",
                schema: "robo");

            migrationBuilder.DropColumn(
                name: "TargetPrice",
                schema: "robo",
                table: "PaperPositions");

            migrationBuilder.DropColumn(
                name: "Metrics_AmbiguousTrades",
                schema: "robo",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Metrics_CVaR95Percent",
                schema: "robo",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Metrics_Calmar",
                schema: "robo",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Metrics_ExpectancyMargin95",
                schema: "robo",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Metrics_ExpectancyPerTrade",
                schema: "robo",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Metrics_Sortino",
                schema: "robo",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Metrics_TurnoverPerYear",
                schema: "robo",
                table: "BacktestRuns");
        }
    }
}
