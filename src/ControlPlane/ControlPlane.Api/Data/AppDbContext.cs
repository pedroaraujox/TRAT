using ControlPlane.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ControlPlane.Api.Domain.Host> Hosts => Set<ControlPlane.Api.Domain.Host>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<BackupPolicy> BackupPolicies => Set<BackupPolicy>();
    public DbSet<AgentRunRequest> AgentRunRequests => Set<AgentRunRequest>();
    public DbSet<PolicyChangeEvent> PolicyChangeEvents => Set<PolicyChangeEvent>();
    public DbSet<AgentConfiguration> AgentConfigurations => Set<AgentConfiguration>();
    public DbSet<PanelUser> PanelUsers => Set<PanelUser>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ControlPlane.Api.Domain.Host>()
            .HasIndex(h => new { h.CustomerId, h.Hostname })
            .IsUnique(false);

        modelBuilder.Entity<Job>()
            .HasIndex(j => new { j.CustomerId, j.HostId, j.StartedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<Alert>()
            .HasIndex(a => new { a.CustomerId, a.Type, a.CreatedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<Alert>()
            .HasIndex(a => new { a.RootCauseKey, a.ResolvedAtUtc, a.LastObservedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<Alert>()
            .HasIndex(a => new { a.CustomerId, a.HostId, a.ResolvedAtUtc, a.CreatedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<BackupPolicy>()
            .HasIndex(p => new { p.CustomerId, p.Name })
            .IsUnique(false);

        modelBuilder.Entity<AgentRunRequest>()
            .HasIndex(r => new { r.CustomerId, r.HostId, r.State, r.RequestedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<PolicyChangeEvent>()
            .HasIndex(e => new { e.PolicyId, e.CreatedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<AgentConfiguration>()
            .HasIndex(c => new { c.CustomerId, c.HostId })
            .IsUnique(false);

        modelBuilder.Entity<PanelUser>()
            .HasIndex(u => u.Email)
            .IsUnique(true);

        modelBuilder.Entity<AuditEvent>()
            .HasIndex(a => new { a.Category, a.CreatedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<AuditEvent>()
            .HasIndex(a => new { a.EntityType, a.EntityId, a.CreatedAtUtc })
            .IsUnique(false);
    }
}
