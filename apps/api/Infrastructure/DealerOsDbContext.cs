using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Organizations;
using DealerOS.Modules.Vehicles.Domain;
using Microsoft.EntityFrameworkCore;

namespace DealerOS.Api.Infrastructure;

public sealed class DealerOsDbContext(DbContextOptions<DealerOsDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<UserBranchAccess> UserBranchAccess => Set<UserBranchAccess>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleStatusHistory> VehicleStatusHistory => Set<VehicleStatusHistory>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations", "organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Branch>(entity =>
        {
            entity.ToTable("branches", "organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Code).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_branches_organization_id");
            entity.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("ux_branches_organization_code");
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("users", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Permissions).HasMaxLength(2000).IsRequired();
            entity.Ignore(x => x.PermissionSet);
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_users_organization_id");
            entity.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_users_email");
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserBranchAccess>(entity =>
        {
            entity.ToTable("user_branch_access", "identity");
            entity.HasKey(x => new { x.OrganizationId, x.UserId, x.BranchId });
            entity.HasOne<UserAccount>().WithMany(x => x.BranchAccess)
                .HasForeignKey(x => new { x.OrganizationId, x.UserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("vehicles", "vehicles", table =>
            {
                table.HasCheckConstraint("ck_vehicles_vin", "\"Vin\" ~ '^[A-HJ-NPR-Z0-9]{17}$'");
                table.HasCheckConstraint("ck_vehicles_year", "\"Year\" >= 1950 AND \"Year\" <= 9999");
                table.HasCheckConstraint("ck_vehicles_mileage", "\"MileageKm\" >= 0 AND \"MileageKm\" <= 3000000");
                table.HasCheckConstraint("ck_vehicles_purchase_amount", "\"PlannedPurchaseAmount\" > 0");
                table.HasCheckConstraint("ck_vehicles_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("ck_vehicles_status", "\"Status\" IN (1, 2)");
                table.HasCheckConstraint("ck_vehicles_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_vehicles_acceptance_state", "(\"Status\" = 1 AND \"AcceptedAt\" IS NULL AND \"StockNumber\" IS NULL) OR (\"Status\" = 2 AND \"AcceptedAt\" IS NOT NULL AND \"StockNumber\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_vehicles_organization_id");
            entity.Property(x => x.Vin).HasMaxLength(17).IsRequired();
            entity.Property(x => x.Make).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Model).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PlannedPurchaseAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.StockNumber).HasMaxLength(50);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.Vin }).IsUnique().HasDatabaseName("ux_vehicles_organization_vin");
            entity.HasIndex(x => new { x.OrganizationId, x.StockNumber }).IsUnique().HasDatabaseName("ux_vehicles_organization_stock_number");
            entity.HasIndex(x => new { x.OrganizationId, x.BranchId, x.CreatedAt }).HasDatabaseName("ix_vehicles_tenant_branch_created");
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<VehicleStatusHistory>(entity =>
        {
            entity.ToTable("status_history", "vehicles", table =>
            {
                table.HasCheckConstraint("ck_status_history_from", "\"FromStatus\" IS NULL OR \"FromStatus\" IN (1, 2)");
                table.HasCheckConstraint("ck_status_history_to", "\"ToStatus\" IN (1, 2)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId, x.ChangedAt });
            entity.HasOne<Vehicle>().WithMany(x => x.StatusHistory)
                .HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ChangedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("events", "audit");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Operation).HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OldValue).HasColumnType("jsonb");
            entity.Property(x => x.NewValue).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.EntityType, x.EntityId, x.OccurredAt });
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
