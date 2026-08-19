using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SyncStreamAPI.DataContext.Migrations
{
    /// <summary>
    ///     Adds the one row of settings that belong to the server rather than to a person or a room.
    /// </summary>
    /// <remarks>
    ///     The row is inserted here rather than declared as seed data on the model. Seed data is for rows the
    ///     model owns and expects to find exactly as declared, and this one is edited by an administrator: EF
    ///     would compare it against the declaration and could emit an update that puts their choice back.
    ///     Declaring it also rebuilt the category list on every model build, which made the model appear to
    ///     change each time and stopped the application starting.
    /// </remarks>
    public partial class AddServerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerSettings",
                columns: table => new
                {
                    ID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SponsorBlockSkipCategories = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerSettings", x => x.ID);
                });

            // Written out rather than taken from the constant the application reads, because a migration
            // describes what happened at one point in time: changing that default later must not rewrite what
            // this migration did. Sponsor and self-promotion only, where the client previously skipped every
            // category except filler.
            migrationBuilder.InsertData(
                table: "ServerSettings",
                columns: new[] { "ID", "SponsorBlockSkipCategories" },
                values: new object[] { 1, new[] { "sponsor", "selfpromo" } });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerSettings");
        }
    }
}
