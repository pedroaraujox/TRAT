using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Data;

public static class SchemaBootstrapper
{
    public static async Task EnsureExtendedSchemaAsync(AppDbContext db)
    {
        await TryAddColumnAsync(db, tableName: "Customers", columnName: "AgentEnrollmentTokenHash", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "Hosts", columnName: "BootstrapIncludePathsCsv", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "Hosts", columnName: "BootstrapExcludePathsCsv", columnTypeSql: "TEXT");

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BackupPolicies" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BackupPolicies" PRIMARY KEY,
                "CustomerId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "ScopeType" TEXT NOT NULL,
                "HostId" TEXT NULL,
                "IncludePathsCsv" TEXT NOT NULL,
                "ExcludePathsCsv" TEXT NULL,
                "ScheduleDaysCsv" TEXT NOT NULL,
                "StartTimeLocal" TEXT NOT NULL,
                "MaxRuntimeMinutes" INTEGER NOT NULL,
                "CpuLimitPercent" INTEGER NOT NULL,
                "NetworkLimitMbit" INTEGER NOT NULL,
                "Enabled" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AgentConfigurations" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AgentConfigurations" PRIMARY KEY,
                "CustomerId" TEXT NOT NULL,
                "HostId" TEXT NOT NULL,
                "PolicyId" TEXT NULL,
                "AgentVersion" TEXT NOT NULL,
                "ServiceStatus" TEXT NOT NULL,
                "TlsMode" TEXT NOT NULL,
                "PrecheckTlsOk" INTEGER NOT NULL,
                "PrecheckDiskOk" INTEGER NOT NULL,
                "PrecheckCredentialOk" INTEGER NOT NULL,
                "StagingPath" TEXT NOT NULL,
                "CredentialTargetName" TEXT NOT NULL,
                "UploadMode" TEXT NOT NULL,
                "LastConfigSyncAtUtc" TEXT NULL,
                "LastPrecheckAtUtc" TEXT NULL,
                "LastPrecheckMessage" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_BackupPolicies_CustomerId_Name"
            ON "BackupPolicies" ("CustomerId", "Name");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_AgentConfigurations_CustomerId_HostId"
            ON "AgentConfigurations" ("CustomerId", "HostId");
            """);
    }

    private static async Task TryAddColumnAsync(AppDbContext db, string tableName, string columnName, string columnTypeSql)
    {
        try
        {
            var sql = "ALTER TABLE \"" + tableName + "\" ADD COLUMN \"" + columnName + "\" " + columnTypeSql + " NULL;";
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch
        {
        }
    }
}
