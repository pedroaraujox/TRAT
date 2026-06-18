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
    public DbSet<PolicyChangeEvent> PolicyChangeEvents => Set<PolicyChangeEvent>();
    public DbSet<AgentConfiguration> AgentConfigurations => Set<AgentConfiguration>();

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

        modelBuilder.Entity<BackupPolicy>()
            .HasIndex(p => new { p.CustomerId, p.Name })
            .IsUnique(false);

        modelBuilder.Entity<PolicyChangeEvent>()
            .HasIndex(e => new { e.PolicyId, e.CreatedAtUtc })
            .IsUnique(false);

        modelBuilder.Entity<AgentConfiguration>()
            .HasIndex(c => new { c.CustomerId, c.HostId })
            .IsUnique(false);
    }
}
