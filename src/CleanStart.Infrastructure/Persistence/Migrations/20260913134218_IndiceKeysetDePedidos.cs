using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanStart.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndiceKeysetDePedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_orders_created_at_id",
                table: "orders",
                columns: new[] { "created_at", "id" },
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_created_at_id",
                table: "orders");
        }
    }
}
