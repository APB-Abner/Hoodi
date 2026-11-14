using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoodi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotPublicationSkeleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SnapshotPublications",
                columns: table => new
                {
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PublishedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    CsvBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ManifestBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    FixtureBytes = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotPublications", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "SnapshotHeads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SnapshotHeads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SnapshotHeads_SnapshotPublications_Version",
                        column: x => x.Version,
                        principalTable: "SnapshotPublications",
                        principalColumn: "Version",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SnapshotHeads_Version",
                table: "SnapshotHeads",
                column: "Version");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SnapshotHeads");

            migrationBuilder.DropTable(
                name: "SnapshotPublications");
        }
    }
}
