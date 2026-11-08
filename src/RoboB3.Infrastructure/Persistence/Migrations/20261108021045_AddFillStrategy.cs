using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoboB3.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFillStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StrategyKey",
                schema: "robo",
                table: "PaperFills",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // Decisões gravadas antes do contexto existir ficaram com texto vazio no lugar de JSON.
            migrationBuilder.Sql("UPDATE [robo].[TradingDecisions] SET [Parameters] = N'{}' WHERE [Parameters] = N''");
            migrationBuilder.Sql("UPDATE [robo].[TradingDecisions] SET [Features] = N'[]' WHERE [Features] = N''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StrategyKey",
                schema: "robo",
                table: "PaperFills");
        }
    }
}
