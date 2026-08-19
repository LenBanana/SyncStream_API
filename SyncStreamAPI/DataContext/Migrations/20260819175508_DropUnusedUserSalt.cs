using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SyncStreamAPI.DataContext.Migrations
{
    /// <summary>
    ///     Drops a column nothing read.
    /// </summary>
    /// <remarks>
    ///     It held a random value per account and was applied to one thing: a session token derived from the
    ///     account and a device string, which was replaced by a random token. Nothing has used it since, and a
    ///     column named for a salt sitting next to a password column reads as though the password were salted
    ///     with it, which it never was. The values are random and referenced by nothing, so there is nothing to
    ///     preserve.
    /// </remarks>
    public partial class DropUnusedUserSalt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "usersalt",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "usersalt",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
