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
    public DbSet<ParseMetric> ParseMetrics => Set<ParseMetric>();
    public DbSet<FailedParseJob> FailedParseJobs => Set<FailedParseJob>();

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
            entity.Property(user => user.ImPercent).HasColumnName("im").HasPrecision(12, 2);
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

        modelBuilder.Entity<ParseMetric>(entity =>
        {
            entity.ToTable("parse_metrics");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).HasColumnName("id");

            entity.Property(m => m.UserId).HasColumnName("user_id");
            entity.Property(m => m.ParseJobId).HasColumnName("parse_job_id");

            entity.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(m => m.ParseJob).WithMany().HasForeignKey(m => m.ParseJobId).OnDelete(DeleteBehavior.SetNull);

            entity.Property(m => m.UserUsername).HasColumnName("user_username").HasMaxLength(128).IsRequired();
            entity.Property(m => m.UserName).HasColumnName("user_name").HasMaxLength(255);

            entity.Property(m => m.Vendor).HasColumnName("vendor").HasMaxLength(64).IsRequired();
            entity.Property(m => m.ParserSlug).HasColumnName("parser_slug").HasMaxLength(128).IsRequired();

            entity.Property(m => m.SourceFilename).HasColumnName("source_filename").HasColumnType("TEXT COLLATE NOCASE").HasMaxLength(255).IsRequired();
            entity.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            entity.Property(m => m.QuotedTotal).HasColumnName("quoted_total").HasPrecision(14, 2);
            entity.Property(m => m.ComputedTotal).HasColumnName("computed_total").HasPrecision(14, 2).IsRequired();
            entity.Property(m => m.TotalsMatch).HasColumnName("totals_match").IsRequired();
            entity.Property(m => m.FxRate).HasColumnName("fx_rate").HasPrecision(12, 4).IsRequired();
            entity.Property(m => m.Margin).HasColumnName("margin").HasPrecision(12, 2).IsRequired();

            entity.Property(m => m.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(m => m.CreatedAt).HasDatabaseName("ix_parse_metrics_created_at");
            entity.HasIndex(m => new { m.Vendor, m.CreatedAt }).HasDatabaseName("ix_parse_metrics_vendor_created_at");
            entity.HasIndex(m => new { m.ParserSlug, m.CreatedAt }).HasDatabaseName("ix_parse_metrics_parser_slug_created_at");
            entity.HasIndex(m => new { m.UserId, m.CreatedAt }).HasDatabaseName("ix_parse_metrics_user_id_created_at");
        });

        modelBuilder.Entity<FailedParseJob>(entity =>
        {
            entity.ToTable("failed_parse_jobs");
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).HasColumnName("id");

            entity.Property(f => f.UserId).HasColumnName("user_id");
            entity.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.SetNull);

            entity.Property(f => f.UserUsername).HasColumnName("user_username").HasMaxLength(128).IsRequired();
            entity.Property(f => f.UserName).HasColumnName("user_name").HasMaxLength(255);

            entity.Property(f => f.Vendor).HasColumnName("vendor").HasMaxLength(64).IsRequired();
            entity.Property(f => f.ParserSlug).HasColumnName("parser_slug").HasMaxLength(128).IsRequired();

            entity.Property(f => f.SourceFilename).HasColumnName("source_filename").HasColumnType("TEXT COLLATE NOCASE").HasMaxLength(255).IsRequired();
            entity.Property(f => f.SourcePath).HasColumnName("source_path").HasMaxLength(1024).IsRequired();

            entity.Property(f => f.Category).HasColumnName("category").HasMaxLength(32).IsRequired()
                .HasConversion(
                    v => v.ToString().ToLowerInvariant(),
                    v => Enum.Parse<FailureCategory>(v, ignoreCase: true));

            entity.Property(f => f.Stage).HasColumnName("stage").HasMaxLength(128);
            entity.Property(f => f.Hint).HasColumnName("hint").HasMaxLength(512);
            entity.Property(f => f.Message).HasColumnName("message").HasMaxLength(1024);

            entity.Property(f => f.ComputedTotal).HasColumnName("computed_total").HasPrecision(14, 2);
            entity.Property(f => f.QuotedTotal).HasColumnName("quoted_total").HasPrecision(14, 2);

            entity.Property(f => f.ErrorDetail).HasColumnName("error_detail").HasColumnType("TEXT").IsRequired();

            entity.Property(f => f.FxRate).HasColumnName("fx_rate").HasPrecision(12, 4).IsRequired();
            entity.Property(f => f.Margin).HasColumnName("margin").HasPrecision(12, 2).IsRequired();

            entity.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(f => f.CreatedAt).IsDescending().HasDatabaseName("ix_failed_parse_jobs_created_at");
            entity.HasIndex(f => new { f.UserId, f.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_failed_parse_jobs_user_id_created_at");
            entity.HasIndex(f => new { f.Category, f.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_failed_parse_jobs_category_created_at");
        });
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ParseJob>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ParseMetric>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<FailedParseJob>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }
    }
}
