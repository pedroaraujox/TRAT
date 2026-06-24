using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Data;

public static class SchemaBootstrapper
{
    public static async Task EnsureExtendedSchemaAsync(AppDbContext db)
    {
        await TryAddColumnAsync(db, tableName: "Alerts", columnName: "RootCauseKey", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "Alerts", columnName: "Source", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "Alerts", columnName: "LastObservedAtUtc", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "Alerts", columnName: "ResolvedAtUtc", columnTypeSql: "TEXT");
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
        await TryAddColumnAsync(db, tableName: "PanelUsers", columnName: "FailedLoginCount", columnTypeSql: "INTEGER");
        await TryAddColumnAsync(db, tableName: "PanelUsers", columnName: "LastFailedLoginAtUtc", columnTypeSql: "TEXT");
        await TryAddColumnAsync(db, tableName: "PanelUsers", columnName: "LockoutUntilUtc", columnTypeSql: "TEXT");

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Alerts" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Alerts" PRIMARY KEY,
                "CustomerId" TEXT NOT NULL,
                "HostId" TEXT NULL,
                "JobId" TEXT NULL,
                "RootCauseKey" TEXT NOT NULL,
                "Source" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "Severity" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "LastObservedAtUtc" TEXT NOT NULL,
                "AcknowledgedAtUtc" TEXT NULL,
                "ResolvedAtUtc" TEXT NULL
            );
            """);

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
                "FailedLoginCount" INTEGER NOT NULL DEFAULT 0,
                "LastFailedLoginAtUtc" TEXT NULL,
                "LockoutUntilUtc" TEXT NULL,
                "LastLoginAtUtc" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "AuditEvents" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AuditEvents" PRIMARY KEY,
                "Category" TEXT NOT NULL,
                "Action" TEXT NOT NULL,
                "Outcome" TEXT NOT NULL,
                "EntityType" TEXT NOT NULL,
                "EntityId" TEXT NULL,
                "CustomerId" TEXT NULL,
                "HostId" TEXT NULL,
                "ActorUserId" TEXT NULL,
                "ActorEmail" TEXT NULL,
                "ActorDisplayName" TEXT NULL,
                "Route" TEXT NULL,
                "IpAddress" TEXT NULL,
                "UserAgent" TEXT NULL,
                "Message" TEXT NOT NULL,
                "MetadataJson" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_Alerts_CustomerId_Type_CreatedAtUtc"
            ON "Alerts" ("CustomerId", "Type", "CreatedAtUtc");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_Alerts_RootCauseKey_ResolvedAtUtc_LastObservedAtUtc"
            ON "Alerts" ("RootCauseKey", "ResolvedAtUtc", "LastObservedAtUtc");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_Alerts_CustomerId_HostId_ResolvedAtUtc_CreatedAtUtc"
            ON "Alerts" ("CustomerId", "HostId", "ResolvedAtUtc", "CreatedAtUtc");
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
            CREATE INDEX IF NOT EXISTS "IX_AuditEvents_Category_CreatedAtUtc"
            ON "AuditEvents" ("Category", "CreatedAtUtc");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_AuditEvents_EntityType_EntityId_CreatedAtUtc"
            ON "AuditEvents" ("EntityType", "EntityId", "CreatedAtUtc");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_PolicyChangeEvents_PolicyId_CreatedAtUtc"
            ON "PolicyChangeEvents" ("PolicyId", "CreatedAtUtc");
            """);

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "Alerts"
            SET "RootCauseKey" = COALESCE(NULLIF(TRIM("RootCauseKey"), ''), 'legacy:' || "Id"),
                "Source" = COALESCE(NULLIF(TRIM("Source"), ''), 'legacy'),
                "LastObservedAtUtc" = COALESCE("LastObservedAtUtc", "CreatedAtUtc")
            WHERE "RootCauseKey" IS NULL
               OR TRIM("RootCauseKey") = ''
               OR "Source" IS NULL
               OR TRIM("Source") = ''
               OR "LastObservedAtUtc" IS NULL;
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
