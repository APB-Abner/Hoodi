using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoodi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PanicAndAccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PedidosPanico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TitularId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClasseCredencial = table.Column<string>(type: "TEXT", nullable: false),
                    Origem = table.Column<string>(type: "TEXT", nullable: false),
                    RegistradoEm = table.Column<long>(type: "INTEGER", nullable: false),
                    DadosExpiramEm = table.Column<long>(type: "INTEGER", nullable: true),
                    DadosCifrados = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidosPanico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PedidosPanico_Usuarios_TitularId",
                        column: x => x.TitularId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DespachosPanico",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PedidoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContatoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VinculoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TokenPushId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: true),
                    Estado = table.Column<string>(type: "TEXT", nullable: false),
                    AbertoEm = table.Column<long>(type: "INTEGER", nullable: true),
                    Tentativas = table.Column<int>(type: "INTEGER", nullable: false),
                    ProximaTentativa = table.Column<long>(type: "INTEGER", nullable: false),
                    TicketId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DespachosPanico", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DespachosPanico_PedidosPanico_PedidoId",
                        column: x => x.PedidoId,
                        principalTable: "PedidosPanico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DespachosPanico_Estado_ProximaTentativa",
                table: "DespachosPanico",
                columns: new[] { "Estado", "ProximaTentativa" });

            migrationBuilder.CreateIndex(
                name: "IX_DespachosPanico_PedidoId",
                table: "DespachosPanico",
                column: "PedidoId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosPanico_TitularId_IdempotencyKey",
                table: "PedidosPanico",
                columns: new[] { "TitularId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DespachosPanico");

            migrationBuilder.DropTable(
                name: "PedidosPanico");
        }
    }
}
