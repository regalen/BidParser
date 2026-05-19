using BidParser.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace BidParser.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<ParseJob> ParseJobs => Set<ParseJob>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id");
            entity.Property(user => user.Username).HasColumnName("username").HasColumnType("TEXT COLLATE NOCASE").HasMaxLength(128).IsRequired();
            entity.HasIndex(user => user.Username).IsUnique().HasDatabaseName("ix_users_username");
            entity.Property(user => user.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(user => user.Role).HasColumnName("role").HasMaxLength(16).IsRequired()
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<UserRole>(v, ignoreCase: true));
            entity.Property(user => user.MustChangePassword).HasColumnName("must_change_password").IsRequired();
            entity.Property(user => user.DefaultVendor).HasColumnName("default_vendor").HasMaxLength(64);
            entity.Property(user => user.FxRate).HasColumnName("fx_rate").HasPrecision(12, 4);
            entity.Property(user => user.Margin).HasColumnName("margin").HasPrecision(12, 2);
            entity.Property(user => user.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasMany(user => user.ParseJobs)
                .WithOne(job => job.User)
                .HasForeignKey(job => job.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ParseJob>(entity =>
        {
            entity.ToTable("parse_jobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).HasColumnName("id");
            entity.Property(job => job.UserId).HasColumnName("user_id").IsRequired();
            entity.HasIndex(job => job.UserId).HasDatabaseName("ix_parse_jobs_user_id");
            entity.HasIndex(job => new { job.UserId, job.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_parse_jobs_user_id_created_at");
            entity.Property(job => job.Vendor).HasColumnName("vendor").HasMaxLength(64).IsRequired();
            entity.Property(job => job.ParserSlug).HasColumnName("parser_slug").HasMaxLength(128).IsRequired();
            entity.Property(job => job.SourceFilename).HasColumnName("source_filename").HasColumnType("TEXT COLLATE NOCASE").HasMaxLength(255).IsRequired();
            entity.Property(job => job.SourcePath).HasColumnName("source_path").HasMaxLength(1024).IsRequired();
            entity.Property(job => job.OutputPath).HasColumnName("output_path").HasMaxLength(1024).IsRequired();
            entity.Property(job => job.FxRate).HasColumnName("fx_rate").HasPrecision(12, 4).IsRequired();
            entity.Property(job => job.Margin).HasColumnName("margin").HasPrecision(12, 2).IsRequired();
            entity.Property(job => job.ComputedTotal).HasColumnName("computed_total").HasPrecision(14, 2).IsRequired();
            entity.Property(job => job.QuotedTotal).HasColumnName("quoted_total").HasPrecision(14, 2);
            entity.Property(job => job.TotalsMatch).HasColumnName("totals_match").IsRequired();
            entity.Property(job => job.CreatedAt).HasColumnName("created_at").IsRequired();
        });
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ParseJob>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
        }
    }
}
