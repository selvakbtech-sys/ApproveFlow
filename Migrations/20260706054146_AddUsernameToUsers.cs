using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApproveFlow.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] <> ''");

            // Backfill usernames for existing users before enforcing uniqueness.
            // Derive from the email local-part, sanitize to allowed characters,
            // fall back to user{Id}, then resolve duplicates with numeric suffixes.
            migrationBuilder.Sql(@"
-- 1. Seed from the email local-part (before '@'), lowercased.
UPDATE Users
SET Username = LOWER(LEFT(Email,
        CASE WHEN CHARINDEX('@', Email) > 0 THEN CHARINDEX('@', Email) - 1 ELSE LEN(Email) END))
WHERE (Username IS NULL OR Username = '') AND Email IS NOT NULL AND Email <> '';

-- 2. Strip any character outside [A-Za-z0-9_.].
WHILE EXISTS (SELECT 1 FROM Users
              WHERE PATINDEX('%[^A-Za-z0-9_.]%', Username COLLATE Latin1_General_BIN) > 0)
BEGIN
    UPDATE Users
    SET Username = STUFF(Username,
            PATINDEX('%[^A-Za-z0-9_.]%', Username COLLATE Latin1_General_BIN), 1, '')
    WHERE PATINDEX('%[^A-Za-z0-9_.]%', Username COLLATE Latin1_General_BIN) > 0;
END

-- 3. Fallback for anyone still without a username.
UPDATE Users SET Username = 'user' + CAST(UserId AS nvarchar(10))
WHERE Username IS NULL OR Username = '';

-- 4. Resolve duplicates: keep the first, suffix the rest (1, 2, ...); repeat
--    until every username is unique (handles suffixes that collide again).
WHILE EXISTS (SELECT 1 FROM Users GROUP BY Username HAVING COUNT(*) > 1)
BEGIN
    ;WITH d AS (
        SELECT UserId, Username,
               ROW_NUMBER() OVER (PARTITION BY Username ORDER BY UserId) AS rn
        FROM Users
    )
    UPDATE u
    SET Username = u.Username + CAST(d.rn - 1 AS nvarchar(10))
    FROM Users u
    JOIN d ON u.UserId = d.UserId
    WHERE d.rn > 1;
END
");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }
    }
}
