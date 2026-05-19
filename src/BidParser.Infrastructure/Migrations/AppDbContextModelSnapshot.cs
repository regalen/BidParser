using System;
using BidParser.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace BidParser.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
public sealed class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");

        modelBuilder.Entity("BidParser.Infrastructure.Entities.ParseJob", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER")
                .HasColumnName("id");

            entity.Property<decimal>("ComputedTotal")
                .HasPrecision(14, 2)
                .HasColumnType("TEXT")
                .HasColumnName("computed_total");

            entity.Property<DateTime>("CreatedAt")
                .HasColumnType("TEXT")
                .HasColumnName("created_at");

            entity.Property<decimal>("FxRate")
                .HasPrecision(12, 4)
                .HasColumnType("TEXT")
                .HasColumnName("fx_rate");

            entity.Property<decimal>("Margin")
                .HasPrecision(12, 2)
                .HasColumnType("TEXT")
                .HasColumnName("margin");

            entity.Property<string>("OutputPath")
                .IsRequired()
                .HasMaxLength(1024)
                .HasColumnType("TEXT")
                .HasColumnName("output_path");

            entity.Property<string>("ParserSlug")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("TEXT")
                .HasColumnName("parser_slug");

            entity.Property<decimal?>("QuotedTotal")
                .HasPrecision(14, 2)
                .HasColumnType("TEXT")
                .HasColumnName("quoted_total");

            entity.Property<string>("SourceFilename")
                .IsRequired()
                .HasMaxLength(255)
                .HasColumnType("TEXT COLLATE NOCASE")
                .HasColumnName("source_filename");

            entity.Property<string>("SourcePath")
                .IsRequired()
                .HasMaxLength(1024)
                .HasColumnType("TEXT")
                .HasColumnName("source_path");

            entity.Property<bool>("TotalsMatch")
                .HasColumnType("INTEGER")
                .HasColumnName("totals_match");

            entity.Property<int>("UserId")
                .HasColumnType("INTEGER")
                .HasColumnName("user_id");

            entity.Property<string>("Vendor")
                .IsRequired()
                .HasMaxLength(64)
                .HasColumnType("TEXT")
                .HasColumnName("vendor");

            entity.HasKey("Id");
            entity.HasIndex("UserId").HasDatabaseName("ix_parse_jobs_user_id");
            entity.HasIndex("UserId", "CreatedAt")
                .IsDescending(false, true)
                .HasDatabaseName("ix_parse_jobs_user_id_created_at");
            entity.ToTable("parse_jobs", (string)null);
        });

        modelBuilder.Entity("BidParser.Infrastructure.Entities.User", entity =>
        {
            entity.Property<int>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER")
                .HasColumnName("id");

            entity.Property<DateTime>("CreatedAt")
                .HasColumnType("TEXT")
                .HasColumnName("created_at");

            entity.Property<string>("DefaultVendor")
                .HasMaxLength(64)
                .HasColumnType("TEXT")
                .HasColumnName("default_vendor");

            entity.Property<decimal?>("FxRate")
                .HasPrecision(12, 4)
                .HasColumnType("TEXT")
                .HasColumnName("fx_rate");

            entity.Property<decimal?>("Margin")
                .HasPrecision(12, 2)
                .HasColumnType("TEXT")
                .HasColumnName("margin");

            entity.Property<bool>("MustChangePassword")
                .HasColumnType("INTEGER")
                .HasColumnName("must_change_password");

            entity.Property<string>("Name")
                .HasMaxLength(255)
                .HasColumnType("TEXT")
                .HasColumnName("name");

            entity.Property<string>("PasswordHash")
                .IsRequired()
                .HasMaxLength(255)
                .HasColumnType("TEXT")
                .HasColumnName("password_hash");

            entity.Property<string>("Role")
                .IsRequired()
                .HasMaxLength(16)
                .HasColumnType("TEXT")
                .HasColumnName("role");

            entity.Property<DateTime>("UpdatedAt")
                .HasColumnType("TEXT")
                .HasColumnName("updated_at");

            entity.Property<string>("Username")
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnType("TEXT COLLATE NOCASE")
                .HasColumnName("username");

            entity.HasKey("Id");
            entity.HasIndex("Username").IsUnique().HasDatabaseName("ix_users_username");
            entity.ToTable("users", (string)null);
        });

        modelBuilder.Entity("BidParser.Infrastructure.Entities.ParseJob", entity =>
        {
            entity.HasOne("BidParser.Infrastructure.Entities.User", "User")
                .WithMany("ParseJobs")
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();

            entity.Navigation("User");
        });

        modelBuilder.Entity("BidParser.Infrastructure.Entities.User", entity =>
        {
            entity.Navigation("ParseJobs");
        });
    }
}
