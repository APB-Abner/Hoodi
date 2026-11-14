using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoodi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cadastros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiraEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumidoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cadastros", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Emails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConteudoCifrado = table.Column<string>(type: "TEXT", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EnviadoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Emails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    SenhaHash = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Redefinicoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiraEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConsumidoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Redefinicoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Redefinicoes_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiraEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiraAbsolutoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevogadoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessoes_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    SessaoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsadoEmUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReplayCifrado = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Hash);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Sessoes_SessaoId",
                        column: x => x.SessaoId,
                        principalTable: "Sessoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cadastros_Email",
                table: "Cadastros",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cadastros_TokenHash",
                table: "Cadastros",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Redefinicoes_TokenHash",
                table: "Redefinicoes",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Redefinicoes_UsuarioId",
                table: "Redefinicoes",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_SessaoId",
                table: "RefreshTokens",
                column: "SessaoId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessoes_UsuarioId",
                table: "Sessoes",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cadastros");

            migrationBuilder.DropTable(
                name: "Emails");

            migrationBuilder.DropTable(
                name: "Redefinicoes");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "Sessoes");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
