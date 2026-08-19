using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncStreamAPI.DataContext.Migrations
{
    /// <summary>
    ///     Stops storing session tokens and starts storing their digests.
    /// </summary>
    /// <remarks>
    ///     The existing tokens are hashed in place rather than discarded, so nobody is signed out by this. That
    ///     is the only reason the data steps are here instead of in application code: the plaintext has to be
    ///     read once, and this is the last moment it exists.
    ///     <para>
    ///         Uses PostgreSQL's built-in <c>sha256</c>, which needs server version 11 or newer. On anything
    ///         older this migration fails outright and the application does not start, which is the correct
    ///         outcome: the alternative is a silent half-migration where every session is a row nobody can match.
    ///     </para>
    /// </remarks>
    public partial class HashRememberTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Token",
                table: "RememberTokens",
                newName: "TokenHash");

            // Added nullable so the existing rows can be filled from what they already hold, rather than from a
            // placeholder date that would read as "expired in the year 1" for every session in the table.
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresUtc",
                table: "RememberTokens",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedUtc",
                table: "RememberTokens",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedUtc",
                table: "RememberTokens",
                type: "timestamp without time zone",
                nullable: true);

            // A row with no token could never have authenticated anything.
            migrationBuilder.Sql(@"
                DELETE FROM ""RememberTokens""
                WHERE ""TokenHash"" IS NULL OR ""TokenHash"" = '';");

            // The lifetime these were issued under, which until now lived only in code.
            migrationBuilder.Sql(@"
                UPDATE ""RememberTokens""
                SET ""ExpiresUtc"" = ""Created"" + INTERVAL '30 days'
                WHERE ""ExpiresUtc"" IS NULL;");

            migrationBuilder.Sql(@"
                UPDATE ""RememberTokens""
                SET ""TokenHash"" = encode(sha256(convert_to(""TokenHash"", 'UTF8')), 'hex');");

            // The tokens this table used to hold were derived from the account and a device string, so the same
            // account on the same device produced the same string every time and duplicates are expected. They
            // have to go before the digest can be made unique. The newest row per digest is the one kept.
            migrationBuilder.Sql(@"
                DELETE FROM ""RememberTokens""
                WHERE ""ID"" NOT IN (
                    SELECT MAX(""ID"") FROM ""RememberTokens"" GROUP BY ""TokenHash""
                );");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresUtc",
                table: "RememberTokens",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RememberTokens_TokenHash",
                table: "RememberTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The tokens themselves are not recoverable from their digests, so going back leaves a column that
            // cannot authenticate anyone. Every session ends; nothing is silently broken.
            migrationBuilder.DropIndex(
                name: "IX_RememberTokens_TokenHash",
                table: "RememberTokens");

            migrationBuilder.Sql(@"DELETE FROM ""RememberTokens"";");

            migrationBuilder.DropColumn(
                name: "ExpiresUtc",
                table: "RememberTokens");

            migrationBuilder.DropColumn(
                name: "LastUsedUtc",
                table: "RememberTokens");

            migrationBuilder.DropColumn(
                name: "RevokedUtc",
                table: "RememberTokens");

            migrationBuilder.RenameColumn(
                name: "TokenHash",
                table: "RememberTokens",
                newName: "Token");
        }
    }
}
