using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TradingDecisions",
                schema: "robo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Time = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Contract = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StrategyKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BarTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Signal = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Probability = table.Column<double>(type: "float", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Stop = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Sizing = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingDecisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradingDecisions_Mode_Time",
                schema: "robo",
                table: "TradingDecisions",
                columns: new[] { "Mode", "Time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradingDecisions",
                schema: "robo");
        }
    }
}
