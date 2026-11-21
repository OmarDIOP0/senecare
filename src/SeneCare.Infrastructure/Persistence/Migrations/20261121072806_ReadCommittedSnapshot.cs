using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SQL Server makes a read wait for any write in progress on the same rows (PostgreSQL does not): with several
    /// visitors at once, pages queue behind each other and can time out. READ_COMMITTED_SNAPSHOT lets reads see the
    /// last committed version instead, like PostgreSQL. A shared host may refuse ALTER DATABASE: then the site still
    /// starts (the message is printed), only slower under load.
    /// </summary>
    public partial class ReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            migrationBuilder.Sql(
                """
                BEGIN TRY
                    IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0
                        ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
                END TRY
                BEGIN CATCH
                    PRINT 'READ_COMMITTED_SNAPSHOT not enabled: ' + ERROR_MESSAGE();
                END CATCH
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);
            migrationBuilder.Sql("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;", suppressTransaction: true);
        }
    }
}
