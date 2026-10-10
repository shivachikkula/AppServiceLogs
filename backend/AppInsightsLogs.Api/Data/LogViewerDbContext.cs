using Microsoft.EntityFrameworkCore;

namespace AppInsightsLogs.Api.Data;

public sealed class LogViewerDbContext(DbContextOptions<LogViewerDbContext> options) : DbContext(options)
{
    public DbSet<UserApplication> UserApplications => Set<UserApplication>();

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
    }
}
