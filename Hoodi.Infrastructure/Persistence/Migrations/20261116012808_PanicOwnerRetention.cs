using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoodi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PanicOwnerRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DadosTitularExpiramEm",
                table: "PedidosPanico",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DadosTitularExpiramEm",
                table: "PedidosPanico");
        }
    }
}
