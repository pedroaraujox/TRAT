using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Data;

public static class SchemaBootstrapper
{
    public static async Task EnsureExtendedSchemaAsync(AppDbContext db)
    {
        await TryAddColumnAsync(db, tableName: "Customers", columnName: "AgentEnrollmentTokenHash", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "Hosts", columnName: "BootstrapIncludePathsCsv", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "Hosts", columnName: "BootstrapExcludePathsCsv", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "BackupPolicies", columnName: "PolicyKind", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "BackupPolicies", columnName: "OriginHostId", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "BackupPolicies", columnName: "AwsRegion", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "BackupPolicies", columnName: "S3BucketName", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "BackupPolicies", columnName: "S3KeyPrefix", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "BackupPolicies", columnName: "LastChangedAtUtc", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "AgentConfigurations", columnName: "EffectivePolicyId", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "AgentConfigurations", columnName: "EffectivePolicyName", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "AgentConfigurations", columnName: "EffectivePolicyKind", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "AgentConfigurations", columnName: "EffectivePolicySource", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "AgentConfigurations", columnName: "EffectivePolicyLastChangedAtUtc", columnTypeSql: "TEXT");

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BackupPolicies" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_BackupPolicies" PRIMARY KEY,
                "CustomerId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "PolicyKind" TEXT NOT NULL DEFAULT 'operational',
                "ScopeType" TEXT NOT NULL,
                "HostId" TEXT NULL,
                "OriginHostId" TEXT NULL,
                "IncludePathsCsv" TEXT NOT NULL,
                "ExcludePathsCsv" TEXT NULL,
                "AwsRegion" TEXT NULL,
                "S3BucketName" TEXT NULL,
                "S3KeyPrefix" TEXT NULL,
                "ScheduleDaysCsv" TEXT NOT NULL,
                "StartTimeLocal" TEXT NOT NULL,
                "MaxRuntimeMinutes" INTEGER NOT NULL,
                "CpuLimitPercent" INTEGER NOT NULL,
                "NetworkLimitMbit" INTEGER NOT NULL,
                "Enabled" INTEGER NOT NULL,
                "LastChangedAtUtc" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "PolicyChangeEvents" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PolicyChangeEvents" PRIMARY KEY,
                "PolicyId" TEXT NOT NULL,
                "EventType" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AgentRunRequests" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AgentRunRequests" PRIMARY KEY,
                "CustomerId" TEXT NOT NULL,
                "HostId" TEXT NOT NULL,
                "TriggerType" TEXT NOT NULL,
                "State" TEXT NOT NULL,
                "RequestedBy" TEXT NOT NULL,
                "RequestedAtUtc" TEXT NOT NULL,
                "ClaimedAtUtc" TEXT NULL,
                "CompletedAtUtc" TEXT NULL,
                "JobId" TEXT NULL,
                "FailureMessage" TEXT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AgentConfigurations" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AgentConfigurations" PRIMARY KEY,
                "CustomerId" TEXT NOT NULL,
                "HostId" TEXT NOT NULL,
                "PolicyId" TEXT NULL,
                "EffectivePolicyId" TEXT NULL,
                "EffectivePolicyName" TEXT NULL,
                "EffectivePolicyKind" TEXT NULL,
                "EffectivePolicySource" TEXT NULL,
                "EffectivePolicyLastChangedAtUtc" TEXT NULL,
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
            CREATE TABLE IF NOT EXISTS "PanelUsers" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_PanelUsers" PRIMARY KEY,
                "Email" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "Role" TEXT NOT NULL,
                "PasswordHash" TEXT NOT NULL,
                "PasswordSalt" TEXT NOT NULL,
                "PasswordIterations" INTEGER NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "LastLoginAtUtc" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_BackupPolicies_CustomerId_Name"
            ON "BackupPolicies" ("CustomerId", "Name");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_AgentRunRequests_CustomerId_HostId_State_RequestedAtUtc"
            ON "AgentRunRequests" ("CustomerId", "HostId", "State", "RequestedAtUtc");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_AgentConfigurations_CustomerId_HostId"
            ON "AgentConfigurations" ("CustomerId", "HostId");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_PanelUsers_Email"
            ON "PanelUsers" ("Email");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_PolicyChangeEvents_PolicyId_CreatedAtUtc"
            ON "PolicyChangeEvents" ("PolicyId", "CreatedAtUtc");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "BackupPolicies"
            SET "PolicyKind" = COALESCE(NULLIF(TRIM("PolicyKind"), ''), 'operational')
            WHERE "PolicyKind" IS NULL OR TRIM("PolicyKind") = '';
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
