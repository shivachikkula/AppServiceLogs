using Microsoft.EntityFrameworkCore;

namespace AppInsightsLogs.Api.Data;

public sealed class LogViewerDbContext(DbContextOptions<LogViewerDbContext> options) : DbContext(options)
{
    public DbSet<UserApplication> UserApplications => Set<UserApplication>();
    public DbSet<QueryAuditEntry> QueryAudit => Set<QueryAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Keep in sync with database/schema.sql.
        modelBuilder.Entity<UserApplication>(entity =>
        {
            entity.ToTable("UserApplications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ApplicationName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AppKey).HasMaxLength(127).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(e => new { e.Email, e.AppKey }).IsUnique();
        });

        modelBuilder.Entity<QueryAuditEntry>(entity =>
        {
            entity.ToTable("QueryAudit");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.AppKey).HasMaxLength(127).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Text).IsRequired();
            entity.Property(e => e.Error).HasMaxLength(2000);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.Email, e.Timestamp });
        });
    }
}
