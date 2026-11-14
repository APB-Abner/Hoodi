using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoodi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContactsAndPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Convites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TitularId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodigoHash = table.Column<string>(type: "TEXT", nullable: false),
                    CriadoEmUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiraEmUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Estado = table.Column<int>(type: "INTEGER", nullable: false),
                    ResgatadoPorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EncerradoEmUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Convites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Convites_Usuarios_ResgatadoPorId",
                        column: x => x.ResgatadoPorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Convites_Usuarios_TitularId",
                        column: x => x.TitularId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TokensPush",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstalacaoId = table.Column<string>(type: "TEXT", nullable: false),
                    Plataforma = table.Column<string>(type: "TEXT", nullable: false),
                    ExpoTokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    TokenCifrado = table.Column<string>(type: "TEXT", nullable: false),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    AtualizadoEmUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokensPush", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TokensPush_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vinculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TitularId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContatoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevogadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vinculos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vinculos_Usuarios_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vinculos_Usuarios_TitularId",
                        column: x => x.TitularId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Convites_CodigoHash",
                table: "Convites",
                column: "CodigoHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Convites_ResgatadoPorId",
                table: "Convites",
                column: "ResgatadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Convites_TitularId_Estado_ExpiraEmUtc",
                table: "Convites",
                columns: new[] { "TitularId", "Estado", "ExpiraEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TokensPush_ExpoTokenHash",
                table: "TokensPush",
                column: "ExpoTokenHash",
                unique: true,
                filter: "\"Ativo\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TokensPush_UsuarioId_InstalacaoId",
                table: "TokensPush",
                columns: new[] { "UsuarioId", "InstalacaoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vinculos_ContatoId",
                table: "Vinculos",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_Vinculos_TitularId_ContatoId",
                table: "Vinculos",
                columns: new[] { "TitularId", "ContatoId" },
                unique: true,
                filter: "\"Status\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Convites");

            migrationBuilder.DropTable(
                name: "TokensPush");

            migrationBuilder.DropTable(
                name: "Vinculos");
        }
    }
}
