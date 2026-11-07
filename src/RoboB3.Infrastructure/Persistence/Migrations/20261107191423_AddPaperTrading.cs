using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaperAccount",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    InitialCapital = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Cash = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PeakEquity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DayStartEquity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CurrentDay = table.Column<DateOnly>(type: "date", nullable: false),
                    CostsPaid = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RealizedProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperAccount", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperFills",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RealizedProfit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperFills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperPositions",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContractSymbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StrategyKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Stop = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Multiplier = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IsFuture = table.Column<bool>(type: "bit", nullable: false),
                    EntryCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Features = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Probability = table.Column<double>(type: "float", nullable: true),
                    EntryTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BarsHeld = table.Column<int>(type: "int", nullable: false),
                    MarkPrice = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MarkTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperPositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyAssignments",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StrategyKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Parameters = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timeframe = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    UseMetaModel = table.Column<bool>(type: "bit", nullable: false),
                    FlattenAtDayEnd = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LastProcessedBar = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyAssignments_Assets_Symbol",
                        column: x => x.Symbol,
                        principalSchema: "robo",
                        principalTable: "Assets",
                        principalColumn: "Symbol",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperFills_Time",
                schema: "robo",
                table: "PaperFills",
                column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyAssignments_Symbol",
                schema: "robo",
                table: "StrategyAssignments",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperAccount",
                schema: "robo");

            migrationBuilder.DropTable(
                name: "PaperFills",
                schema: "robo");

            migrationBuilder.DropTable(
                name: "PaperPositions",
                schema: "robo");

            migrationBuilder.DropTable(
                name: "StrategyAssignments",
                schema: "robo");
        }
    }
}
