using Microsoft.EntityFrameworkCore;

namespace XtreamForge.Infrastructure.Data;

public sealed class XtreamForgeDbContext(DbContextOptions<XtreamForgeDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var settings = modelBuilder.Entity<Setting>();

        settings.ToTable("settings");
        settings.HasKey(setting => setting.Id);
        settings.HasIndex(setting => setting.Key).IsUnique();
        settings.Property(setting => setting.Id).HasColumnName("id");
        settings.Property(setting => setting.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        settings.Property(setting => setting.Value).HasColumnName("value").HasMaxLength(4000);
        settings.Property(setting => setting.Description).HasColumnName("description").HasMaxLength(500);
        settings.Property(setting => setting.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}
