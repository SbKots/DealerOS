using DealerOS.Modules.Crm.Domain;
using DealerOS.Modules.IdentityAccess;
using DealerOS.Modules.Inspections.Domain;
using DealerOS.Modules.Operations.Domain;
using DealerOS.Modules.Organizations;
using DealerOS.Modules.Reconditioning.Domain;
using DealerOS.Modules.Reservations.Domain;
using DealerOS.Modules.Sales.Domain;
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
    public DbSet<InspectionTemplate> InspectionTemplates => Set<InspectionTemplate>();
    public DbSet<InspectionTemplateItem> InspectionTemplateItems => Set<InspectionTemplateItem>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();
    public DbSet<InspectionDefect> InspectionDefects => Set<InspectionDefect>();
    public DbSet<InspectionPhoto> InspectionPhotos => Set<InspectionPhoto>();
    public DbSet<InspectionObjectDeletion> InspectionObjectDeletions => Set<InspectionObjectDeletion>();
    public DbSet<ReconditioningPlan> ReconditioningPlans => Set<ReconditioningPlan>();
    public DbSet<ReconditioningWork> ReconditioningWorks => Set<ReconditioningWork>();
    public DbSet<ReconditioningDefectOmission> ReconditioningDefectOmissions => Set<ReconditioningDefectOmission>();
    public DbSet<ReconditioningDecision> ReconditioningDecisions => Set<ReconditioningDecision>();
    public DbSet<ReconditioningBudgetSnapshot> ReconditioningBudgetSnapshots => Set<ReconditioningBudgetSnapshot>();
    public DbSet<ReconditioningPlanHistory> ReconditioningPlanHistory => Set<ReconditioningPlanHistory>();
    public DbSet<ReconditioningExecution> ReconditioningExecutions => Set<ReconditioningExecution>();
    public DbSet<ExecutionWorkOrder> ExecutionWorkOrders => Set<ExecutionWorkOrder>();
    public DbSet<WorkOrderMaterialMovement> WorkOrderMaterialMovements => Set<WorkOrderMaterialMovement>();
    public DbSet<ExecutionOverrunDecision> ExecutionOverrunDecisions => Set<ExecutionOverrunDecision>();
    public DbSet<ExecutionNotification> ExecutionNotifications => Set<ExecutionNotification>();
    public DbSet<QualityCheck> QualityChecks => Set<QualityCheck>();
    public DbSet<QualityObservation> QualityObservations => Set<QualityObservation>();
    public DbSet<VehicleMedia> VehicleMedia => Set<VehicleMedia>();
    public DbSet<ListingContent> ListingContents => Set<ListingContent>();
    public DbSet<ChannelPublication> ChannelPublications => Set<ChannelPublication>();
    public DbSet<ListingContentHistory> ListingContentHistory => Set<ListingContentHistory>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadActivity> LeadActivities => Set<LeadActivity>();
    public DbSet<LeadStatusHistory> LeadStatusHistory => Set<LeadStatusHistory>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitHistory> VisitHistory => Set<VisitHistory>();
    public DbSet<SalesOffer> SalesOffers => Set<SalesOffer>();
    public DbSet<SalesOfferLineItem> SalesOfferLineItems => Set<SalesOfferLineItem>();
    public DbSet<SalesOfferHistory> SalesOfferHistory => Set<SalesOfferHistory>();
    public DbSet<SalesOfferDecision> SalesOfferDecisions => Set<SalesOfferDecision>();
    public DbSet<ApprovedOfferSnapshot> ApprovedOfferSnapshots => Set<ApprovedOfferSnapshot>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationHistory> ReservationHistory => Set<ReservationHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations", "organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.LeadFirstResponseSlaMinutes).HasDefaultValue(30);
            entity.Property(x => x.SalesAutoApprovalDiscountLimit).HasPrecision(19, 2).HasDefaultValue(50_000m);
            entity.Property(x => x.SalesMinimumMarginAmount).HasPrecision(19, 2).HasDefaultValue(100_000m);
            entity.Property(x => x.RequireIndependentReconditioningApproval).HasDefaultValue(true);
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
                table.HasCheckConstraint("ck_vehicles_status", "\"Status\" BETWEEN 1 AND 9");
                table.HasCheckConstraint("ck_vehicles_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_vehicles_acceptance_state", "(\"Status\" = 1 AND \"AcceptedAt\" IS NULL AND \"StockNumber\" IS NULL) OR (\"Status\" BETWEEN 2 AND 9 AND \"AcceptedAt\" IS NOT NULL AND \"StockNumber\" IS NOT NULL)");
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
                table.HasCheckConstraint("ck_status_history_from", "\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 1 AND 9");
                table.HasCheckConstraint("ck_status_history_to", "\"ToStatus\" BETWEEN 1 AND 9");
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

        modelBuilder.Entity<InspectionTemplate>(entity =>
        {
            entity.ToTable("templates", "inspections", table =>
            {
                table.HasCheckConstraint("ck_inspection_templates_version", "\"Version\" > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_inspection_templates_organization_id");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.Version }).IsUnique()
                .HasDatabaseName("ux_inspection_templates_organization_version");
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<InspectionTemplateItem>(entity =>
        {
            entity.ToTable("template_items", "inspections", table =>
            {
                table.HasCheckConstraint("ck_inspection_template_items_category", "\"Category\" BETWEEN 1 AND 11");
                table.HasCheckConstraint("ck_inspection_template_items_sort", "\"SortOrder\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.HasIndex(x => new { x.OrganizationId, x.TemplateId, x.Key }).IsUnique()
                .HasDatabaseName("ux_inspection_template_items_key");
            entity.HasOne<InspectionTemplate>().WithMany(x => x.Items)
                .HasForeignKey(x => new { x.OrganizationId, x.TemplateId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Inspection>(entity =>
        {
            entity.ToTable("inspections", "inspections", table =>
            {
                table.HasCheckConstraint("ck_inspections_status", "\"Status\" IN (1, 2, 3, 4)");
                table.HasCheckConstraint("ck_inspections_mileage", "\"MileageKm\" BETWEEN 0 AND 3000000");
                table.HasCheckConstraint("ck_inspections_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_inspections_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("ck_inspections_timestamps", "(\"Status\" = 1 AND \"StartedAt\" IS NULL AND \"CompletedAt\" IS NULL) OR (\"Status\" = 2 AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL) OR (\"Status\" = 3 AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL) OR (\"Status\" = 4 AND \"CompletedAt\" IS NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_inspections_organization_id");
            entity.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FinalComment).HasMaxLength(4000);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId }).IsUnique()
                .HasFilter("\"Status\" IN (1, 2)")
                .HasDatabaseName("ux_inspections_active_vehicle");
            entity.HasIndex(x => new { x.OrganizationId, x.BranchId, x.Status, x.CreatedAt })
                .HasDatabaseName("ix_inspections_tenant_branch_status_created");
            entity.HasOne<Vehicle>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.InspectorId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InspectionTemplate>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.TemplateId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Inspection>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.CorrectsInspectionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.Defects).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<InspectionItem>(entity =>
        {
            entity.ToTable("items", "inspections", table =>
            {
                table.HasCheckConstraint("ck_inspection_items_category", "\"Category\" BETWEEN 1 AND 11");
                table.HasCheckConstraint("ck_inspection_items_result", "\"Result\" IN (1, 2, 3, 4)");
                table.HasCheckConstraint("ck_inspection_items_sort", "\"SortOrder\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Comment).HasMaxLength(2000);
            entity.HasIndex(x => new { x.OrganizationId, x.InspectionId, x.Key }).IsUnique()
                .HasDatabaseName("ux_inspection_items_snapshot_key");
            entity.HasOne<Inspection>().WithMany(x => x.Items)
                .HasForeignKey(x => new { x.OrganizationId, x.InspectionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InspectionDefect>(entity =>
        {
            entity.ToTable("defects", "inspections", table =>
            {
                table.HasCheckConstraint("ck_inspection_defects_category", "\"Category\" BETWEEN 1 AND 11");
                table.HasCheckConstraint("ck_inspection_defects_severity", "\"Severity\" IN (1, 2, 3)");
                table.HasCheckConstraint("ck_inspection_defects_amount", "\"EstimatedRepairAmount\" IS NULL OR \"EstimatedRepairAmount\" >= 0");
                table.HasCheckConstraint("ck_inspection_defects_money", "(\"EstimatedRepairAmount\" IS NULL AND \"Currency\" IS NULL) OR (\"EstimatedRepairAmount\" IS NOT NULL AND \"Currency\" ~ '^[A-Z]{3}$')");
                table.HasCheckConstraint("ck_inspection_defects_critical", "\"Severity\" <> 3 OR (\"RepairRequired\" AND \"BlocksSale\")");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_inspection_defects_organization_id");
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Recommendation).HasMaxLength(4000);
            entity.Property(x => x.EstimatedRepairAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.HasIndex(x => new { x.OrganizationId, x.InspectionId, x.Severity });
            entity.HasOne<Inspection>().WithMany(x => x.Defects)
                .HasForeignKey(x => new { x.OrganizationId, x.InspectionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<InspectionPhoto>(entity =>
        {
            entity.ToTable("photos", "inspections", table =>
            {
                table.HasCheckConstraint("ck_inspection_photos_size", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 8388608");
                table.HasCheckConstraint("ck_inspection_photos_type", "\"ContentType\" IN ('image/jpeg', 'image/png', 'image/webp')");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_inspection_photos_organization_id");
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.ObjectKey })
                .HasDatabaseName("ix_inspection_photos_tenant_object_key");
            entity.HasOne<InspectionDefect>().WithMany(x => x.Photos)
                .HasForeignKey(x => new { x.OrganizationId, x.DefectId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InspectionPhoto>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.SourcePhotoId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id })
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        modelBuilder.Entity<InspectionObjectDeletion>(entity =>
        {
            entity.ToTable("object_deletion_queue", "inspections");
            entity.HasKey(x => new { x.OrganizationId, x.ObjectKey });
            entity.Property(x => x.ObjectKey).HasMaxLength(500);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReconditioningPlan>(entity =>
        {
            entity.ToTable("plans", "reconditioning", table =>
            {
                table.HasCheckConstraint("ck_reconditioning_plans_status", "\"Status\" IN (1, 2, 3, 4, 5, 6)");
                table.HasCheckConstraint("ck_reconditioning_plans_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("ck_reconditioning_plans_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_reconditioning_plans_timestamps",
                    "(\"Status\" IN (1, 3) AND \"DecidedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR " +
                    "(\"Status\" = 2 AND \"SubmittedAt\" IS NOT NULL AND \"DecidedAt\" IS NULL AND \"CancelledAt\" IS NULL) OR " +
                    "(\"Status\" IN (4, 5) AND \"SubmittedAt\" IS NOT NULL AND \"DecidedAt\" IS NOT NULL AND \"CancelledAt\" IS NULL) OR " +
                    "(\"Status\" = 6 AND \"CancelledAt\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_reconditioning_plans_organization_id");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId }).IsUnique()
                .HasFilter("\"Status\" IN (1, 2, 3)")
                .HasDatabaseName("ux_reconditioning_plans_active_vehicle");
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId, x.Revision }).IsUnique()
                .HasDatabaseName("ux_reconditioning_plans_vehicle_revision");
            entity.HasIndex(x => new { x.OrganizationId, x.BranchId, x.Status, x.UpdatedAt })
                .HasDatabaseName("ix_reconditioning_plans_tenant_branch_status");
            entity.HasOne<Vehicle>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Inspection>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.SourceInspectionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ReconditioningPlan>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.RevisesPlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.Navigation(x => x.Works).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.Omissions).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.Decisions).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.BudgetSnapshots).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ReconditioningWork>(entity =>
        {
            entity.ToTable("works", "reconditioning", table =>
            {
                table.HasCheckConstraint("ck_reconditioning_works_category", "\"Category\" BETWEEN 1 AND 8");
                table.HasCheckConstraint("ck_reconditioning_works_priority", "\"Priority\" BETWEEN 1 AND 4");
                table.HasCheckConstraint("ck_reconditioning_works_executor", "\"ExecutorType\" IN (1, 2)");
                table.HasCheckConstraint("ck_reconditioning_works_amounts",
                    "\"EstimatedLaborAmount\" >= 0 AND \"EstimatedPartsAmount\" >= 0");
                table.HasCheckConstraint("ck_reconditioning_works_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("ck_reconditioning_works_duration", "\"EstimatedDurationDays\" BETWEEN 1 AND 365");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_reconditioning_works_organization_id");
            entity.Property(x => x.SourceDefectTitle).HasMaxLength(300).IsRequired();
            entity.Property(x => x.SourceDefectDescription).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.SourceDefectSeverity).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.ExecutorName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.EstimatedLaborAmount).HasPrecision(19, 2);
            entity.Property(x => x.EstimatedPartsAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Comment).HasMaxLength(2000);
            entity.HasIndex(x => new { x.OrganizationId, x.PlanId, x.SourceDefectId });
            entity.HasOne<ReconditioningPlan>().WithMany(x => x.Works)
                .HasForeignKey(x => new { x.OrganizationId, x.PlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<InspectionDefect>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.SourceDefectId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReconditioningDefectOmission>(entity =>
        {
            entity.ToTable("defect_omissions", "reconditioning");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.SourceDefectTitle).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.PlanId, x.SourceDefectId }).IsUnique()
                .HasDatabaseName("ux_reconditioning_omissions_plan_defect");
            entity.HasOne<ReconditioningPlan>().WithMany(x => x.Omissions)
                .HasForeignKey(x => new { x.OrganizationId, x.PlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<InspectionDefect>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.SourceDefectId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.DecidedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReconditioningDecision>(entity =>
        {
            entity.ToTable("decisions", "reconditioning", table =>
            {
                table.HasCheckConstraint("ck_reconditioning_decisions_type", "\"Type\" IN (1, 2, 3)");
                table.HasCheckConstraint("ck_reconditioning_decisions_money",
                    "(\"ApprovedLimitAmount\" IS NULL AND \"Currency\" IS NULL) OR " +
                    "(\"ApprovedLimitAmount\" >= 0 AND \"Currency\" ~ '^[A-Z]{3}$')");
            });
            entity.HasKey(x => new { x.OrganizationId, x.PlanId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Reason).HasMaxLength(2000);
            entity.Property(x => x.ApprovedLimitAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.HasIndex(x => new { x.OrganizationId, x.PlanId, x.Id }).IsUnique()
                .HasDatabaseName("ux_reconditioning_decisions_command");
            entity.HasOne<ReconditioningPlan>().WithMany(x => x.Decisions)
                .HasForeignKey(x => new { x.OrganizationId, x.PlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReconditioningBudgetSnapshot>(entity =>
        {
            entity.ToTable("budget_snapshots", "reconditioning", table =>
            {
                table.HasCheckConstraint("ck_reconditioning_snapshots_amounts",
                    "\"LaborAmount\" >= 0 AND \"PartsAmount\" >= 0 AND \"PlannedTotalAmount\" >= 0 AND \"ApprovedLimitAmount\" >= 0");
                table.HasCheckConstraint("ck_reconditioning_snapshots_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_reconditioning_budget_snapshots_organization_id");
            entity.Property(x => x.LaborAmount).HasPrecision(19, 2);
            entity.Property(x => x.PartsAmount).HasPrecision(19, 2);
            entity.Property(x => x.PlannedTotalAmount).HasPrecision(19, 2);
            entity.Property(x => x.ApprovedLimitAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.PlanId }).IsUnique()
                .HasDatabaseName("ux_reconditioning_snapshot_plan");
            entity.HasOne<ReconditioningPlan>().WithMany(x => x.BudgetSnapshots)
                .HasForeignKey(x => new { x.OrganizationId, x.PlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ApprovedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReconditioningPlanHistory>(entity =>
        {
            entity.ToTable("plan_history", "reconditioning", table =>
            {
                table.HasCheckConstraint("ck_reconditioning_history_from",
                    "\"FromStatus\" IS NULL OR \"FromStatus\" IN (1, 2, 3, 4, 5, 6)");
                table.HasCheckConstraint("ck_reconditioning_history_to", "\"ToStatus\" IN (1, 2, 3, 4, 5, 6)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Reason).HasMaxLength(2000);
            entity.HasIndex(x => new { x.OrganizationId, x.PlanId, x.OccurredAt });
            entity.HasOne<ReconditioningPlan>().WithMany(x => x.History)
                .HasForeignKey(x => new { x.OrganizationId, x.PlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReconditioningExecution>(entity =>
        {
            entity.ToTable("executions", "operations", table =>
            {
                table.HasCheckConstraint("ck_operations_executions_status", "\"Status\" IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint("ck_operations_executions_amounts",
                    "\"PlannedAmount\" >= 0 AND \"ApprovedLimitAmount\" >= 0");
                table.HasCheckConstraint("ck_operations_executions_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("ck_operations_executions_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_operations_executions_timestamps",
                    "(\"Status\" = 1 AND \"StartedAt\" IS NULL AND \"CompletedAt\" IS NULL) OR " +
                    "(\"Status\" IN (2, 3) AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NULL) OR " +
                    "(\"Status\" = 4 AND \"StartedAt\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL) OR " +
                    "(\"Status\" = 5 AND \"CompletedAt\" IS NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_operations_executions_organization_id");
            entity.Property(x => x.PlannedAmount).HasPrecision(19, 2);
            entity.Property(x => x.ApprovedLimitAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Ignore(x => x.ActualLaborAmount);
            entity.Ignore(x => x.ActualMaterialAmount);
            entity.Ignore(x => x.ActualExternalAmount);
            entity.Ignore(x => x.ActualTotalAmount);
            entity.Ignore(x => x.VarianceAmount);
            entity.HasIndex(x => new { x.OrganizationId, x.BudgetSnapshotId }).IsUnique()
                .HasDatabaseName("ux_operations_execution_snapshot");
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId, x.Status, x.UpdatedAt })
                .HasDatabaseName("ix_operations_execution_vehicle_status");
            entity.HasOne<Vehicle>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ReconditioningPlan>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.PlanId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ReconditioningBudgetSnapshot>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.BudgetSnapshotId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.WorkOrders).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.OverrunDecisions).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.Notifications).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ExecutionWorkOrder>(entity =>
        {
            entity.ToTable("work_orders", "operations", table =>
            {
                table.HasCheckConstraint("ck_operations_work_orders_status", "\"Status\" BETWEEN 1 AND 6");
                table.HasCheckConstraint("ck_operations_work_orders_settlement", "\"SettlementStatus\" BETWEEN 1 AND 6");
                table.HasCheckConstraint("ck_operations_work_orders_amounts",
                    "\"PlannedLaborAmount\" >= 0 AND \"PlannedPartsAmount\" >= 0 AND " +
                    "\"ActualLaborHours\" >= 0 AND \"ActualLaborAmount\" >= 0 AND \"ActualExternalAmount\" >= 0");
                table.HasCheckConstraint("ck_operations_work_orders_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_operations_work_orders_organization_id");
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ExecutorType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.AssigneeName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.PlannedLaborAmount).HasPrecision(19, 2);
            entity.Property(x => x.PlannedPartsAmount).HasPrecision(19, 2);
            entity.Property(x => x.ActualLaborHours).HasPrecision(19, 2);
            entity.Property(x => x.ActualLaborAmount).HasPrecision(19, 2);
            entity.Property(x => x.ActualExternalAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ContractorName).HasMaxLength(300);
            entity.Property(x => x.InvoiceReference).HasMaxLength(200);
            entity.Property(x => x.SettlementComment).HasMaxLength(2000);
            entity.Property(x => x.BlockReason).HasMaxLength(2000);
            entity.Property(x => x.CompletionComment).HasMaxLength(2000);
            entity.Ignore(x => x.MaterialAmount);
            entity.HasIndex(x => new { x.OrganizationId, x.ExecutionId, x.SourcePlanWorkId }).IsUnique()
                .HasDatabaseName("ux_operations_work_order_plan_work");
            entity.HasIndex(x => new { x.OrganizationId, x.DueAt, x.Status })
                .HasDatabaseName("ix_operations_work_order_due_status");
            entity.HasOne<ReconditioningExecution>().WithMany(x => x.WorkOrders)
                .HasForeignKey(x => new { x.OrganizationId, x.ExecutionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ReconditioningWork>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.SourcePlanWorkId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InspectionDefect>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.SourceDefectId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.SettlementChangedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.Navigation(x => x.MaterialMovements).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<WorkOrderMaterialMovement>(entity =>
        {
            entity.ToTable("material_movements", "operations", table =>
            {
                table.HasCheckConstraint("ck_operations_material_type", "\"Type\" IN (1, 2)");
                table.HasCheckConstraint("ck_operations_material_quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint("ck_operations_material_cost", "\"UnitCost\" >= 0");
                table.HasCheckConstraint("ck_operations_material_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
            });
            entity.HasKey(x => new { x.OrganizationId, x.WorkOrderId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(19, 3);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.UnitCost).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.SupplierName).HasMaxLength(300);
            entity.Ignore(x => x.SignedAmount);
            entity.HasOne<ExecutionWorkOrder>().WithMany(x => x.MaterialMovements)
                .HasForeignKey(x => new { x.OrganizationId, x.WorkOrderId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExecutionOverrunDecision>(entity =>
        {
            entity.ToTable("overrun_decisions", "operations", table =>
            {
                table.HasCheckConstraint("ck_operations_overrun_amounts",
                    "\"ActualAmount\" >= 0 AND \"ApprovedLimitAmount\" >= \"ActualAmount\"");
                table.HasCheckConstraint("ck_operations_overrun_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
            });
            entity.HasKey(x => new { x.OrganizationId, x.ExecutionId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ActualAmount).HasPrecision(19, 2);
            entity.Property(x => x.ApprovedLimitAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            entity.HasOne<ReconditioningExecution>().WithMany(x => x.OverrunDecisions)
                .HasForeignKey(x => new { x.OrganizationId, x.ExecutionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExecutionNotification>(entity =>
        {
            entity.ToTable("notifications", "operations", table =>
                table.HasCheckConstraint("ck_operations_notification_type", "\"Type\" IN (1, 2)"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.DeduplicationKey).HasMaxLength(150).IsRequired();
            entity.HasIndex(x => new { x.OrganizationId, x.DeduplicationKey }).IsUnique()
                .HasDatabaseName("ux_operations_notification_deduplication");
            entity.HasOne<ReconditioningExecution>().WithMany(x => x.Notifications)
                .HasForeignKey(x => new { x.OrganizationId, x.ExecutionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ExecutionWorkOrder>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.WorkOrderId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<QualityCheck>(entity =>
        {
            entity.ToTable("quality_checks", "operations", table =>
            {
                table.HasCheckConstraint("ck_quality_status", "\"Status\" BETWEEN 1 AND 4");
                table.HasCheckConstraint("ck_quality_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("ck_quality_version", "\"Version\" > 0");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_quality_organization_id");
            entity.Property(x => x.ChecklistSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.DecisionComment).HasMaxLength(2000);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.ExecutionId, x.Revision }).IsUnique()
                .HasDatabaseName("ux_quality_execution_revision");
            entity.HasIndex(x => new { x.OrganizationId, x.ExecutionId }).IsUnique()
                .HasFilter("\"Status\" = 1").HasDatabaseName("ux_quality_execution_draft");
            entity.HasOne<ReconditioningExecution>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ExecutionId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DecidedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.Navigation(x => x.Observations).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<QualityObservation>(entity =>
        {
            entity.ToTable("quality_observations", "operations", table =>
                table.HasCheckConstraint("ck_quality_observation_severity", "\"Severity\" BETWEEN 1 AND 3"));
            entity.HasKey(x => new { x.OrganizationId, x.QualityCheckId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever(); entity.Property(x => x.Comment).HasMaxLength(2000);
            entity.HasOne<QualityCheck>().WithMany(x => x.Observations)
                .HasForeignKey(x => new { x.OrganizationId, x.QualityCheckId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ExecutionWorkOrder>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, Id = x.WorkOrderId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne<InspectionDefect>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, Id = x.DefectId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        modelBuilder.Entity<VehicleMedia>(entity =>
        {
            entity.ToTable("vehicle_media", "operations", table =>
            {
                table.HasCheckConstraint("ck_vehicle_media_category", "\"Category\" BETWEEN 1 AND 4");
                table.HasCheckConstraint("ck_vehicle_media_size", "\"SizeBytes\" > 0");
                table.HasCheckConstraint("ck_vehicle_media_sort", "\"SortOrder\" >= 0");
                table.HasCheckConstraint("ck_vehicle_media_version", "\"Version\" > 0");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_vehicle_media_organization_id");
            entity.Property(x => x.ObjectKey).HasMaxLength(600).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.ObjectKey).IsUnique().HasDatabaseName("ux_vehicle_media_object_key");
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId }).IsUnique().HasFilter("\"IsCover\"")
                .HasDatabaseName("ux_vehicle_media_cover");
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId, x.SortOrder });
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ListingContent>(entity =>
        {
            entity.ToTable("listing_contents", "operations", table =>
            {
                table.HasCheckConstraint("ck_listing_status", "\"Status\" IN (1, 2)");
                table.HasCheckConstraint("ck_listing_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("ck_listing_price", "\"PublicPriceAmount\" >= 0");
                table.HasCheckConstraint("ck_listing_version", "\"Version\" > 0");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_listing_organization_id");
            entity.Property(x => x.VehicleMake).HasMaxLength(100).IsRequired();
            entity.Property(x => x.VehicleModel).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Equipment).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Advantages).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.ConditionDescription).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.PublicPriceAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId, x.Revision }).IsUnique()
                .HasDatabaseName("ux_listing_vehicle_revision");
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId }).IsUnique().HasFilter("\"Status\" = 1")
                .HasDatabaseName("ux_listing_vehicle_draft");
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Publications).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ChannelPublication>(entity =>
        {
            entity.ToTable("channel_publications", "operations", table =>
                table.HasCheckConstraint("ck_channel_publication_status", "\"Status\" BETWEEN 1 AND 5"));
            entity.HasKey(x => new { x.OrganizationId, x.ListingContentId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever(); entity.Property(x => x.Channel).HasMaxLength(100);
            entity.Property(x => x.ExternalId).HasMaxLength(200); entity.Property(x => x.ExternalUrl).HasMaxLength(1000);
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.HasIndex(x => new { x.OrganizationId, x.ListingContentId, x.Channel }).IsUnique()
                .HasDatabaseName("ux_channel_publication_listing_channel");
            entity.HasOne<ListingContent>().WithMany(x => x.Publications)
                .HasForeignKey(x => new { x.OrganizationId, x.ListingContentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ListingContentHistory>(entity =>
        {
            entity.ToTable("listing_history", "operations");
            entity.HasKey(x => new { x.OrganizationId, x.ListingContentId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever(); entity.Property(x => x.Action).HasMaxLength(50);
            entity.Property(x => x.Signature).HasColumnType("text");
            entity.Property(x => x.PublicPriceAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.HasIndex(x => new { x.OrganizationId, x.ListingContentId, x.CommandId }).IsUnique()
                .HasDatabaseName("ux_listing_history_command");
            entity.HasOne<ListingContent>().WithMany(x => x.History)
                .HasForeignKey(x => new { x.OrganizationId, x.ListingContentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers", "crm", table =>
            {
                table.HasCheckConstraint("ck_crm_customer_type", "\"Type\" IN (1, 2)");
                table.HasCheckConstraint("ck_crm_customer_channel", "\"PreferredChannel\" BETWEEN 1 AND 3");
                table.HasCheckConstraint("ck_crm_customer_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_crm_customer_consent", "(NOT \"ConsentGiven\" AND NOT \"MarketingConsent\") OR (\"ConsentAt\" IS NOT NULL AND \"ConsentSource\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_crm_customers_organization_id");
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.NormalizedPhone).HasMaxLength(16);
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320);
            entity.Property(x => x.ConsentSource).HasMaxLength(200);
            entity.Property(x => x.MergeReason).HasMaxLength(2000);
            entity.Property(x => x.Version).IsConcurrencyToken(); entity.Ignore(x => x.IsMerged);
            entity.HasIndex(x => new { x.OrganizationId, x.NormalizedPhone });
            entity.HasIndex(x => new { x.OrganizationId, x.NormalizedEmail });
            entity.HasIndex(x => new { x.OrganizationId, x.Name });
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedInBranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.MergedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.OrganizationId, Id = x.MergedIntoCustomerId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("leads", "crm", table =>
            {
                table.HasCheckConstraint("ck_crm_lead_status", "\"Status\" BETWEEN 1 AND 8");
                table.HasCheckConstraint("ck_crm_lead_interest", "\"VehicleId\" IS NOT NULL OR \"SearchCriteria\" IS NOT NULL");
                table.HasCheckConstraint("ck_crm_lead_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_crm_lead_first_response", "\"FirstResponseAt\" IS NULL OR \"AssignedManagerUserId\" IS NOT NULL");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_crm_leads_organization_id");
            entity.Property(x => x.SearchCriteria).HasMaxLength(2000); entity.Property(x => x.Source).HasMaxLength(100);
            entity.Property(x => x.LostReason).HasMaxLength(2000); entity.Property(x => x.NextAction).HasMaxLength(1000);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.BranchId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.OrganizationId, x.AssignedManagerUserId, x.Status });
            entity.HasIndex(x => new { x.OrganizationId, x.FirstResponseDueAt });
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CustomerId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.OrganizationId, Id = x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, Id = x.AssignedManagerUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            entity.Navigation(x => x.Activities).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<LeadActivity>(entity =>
        {
            entity.ToTable("lead_activities", "crm", table =>
            {
                table.HasCheckConstraint("ck_crm_activity_type", "\"Type\" BETWEEN 1 AND 5");
                table.HasCheckConstraint("ck_crm_activity_direction", "\"Direction\" BETWEEN 1 AND 3");
            });
            entity.HasKey(x => new { x.OrganizationId, x.LeadId, x.Id }); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Result).HasMaxLength(100); entity.Property(x => x.Summary).HasMaxLength(1000);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId, x.CommandId }).IsUnique()
                .HasDatabaseName("ux_crm_activity_command");
            entity.HasOne<Lead>().WithMany(x => x.Activities).HasForeignKey(x => new { x.OrganizationId, x.LeadId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LeadStatusHistory>(entity =>
        {
            entity.ToTable("lead_history", "crm", table =>
            {
                table.HasCheckConstraint("ck_crm_history_from", "\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 1 AND 8");
                table.HasCheckConstraint("ck_crm_history_to", "\"ToStatus\" BETWEEN 1 AND 8");
            });
            entity.HasKey(x => new { x.OrganizationId, x.LeadId, x.Id }); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Operation).HasMaxLength(50); entity.Property(x => x.Signature).HasColumnType("text");
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId, x.CommandId }).IsUnique()
                .HasDatabaseName("ux_crm_lead_history_command");
            entity.HasOne<Lead>().WithMany(x => x.History).HasForeignKey(x => new { x.OrganizationId, x.LeadId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Visit>(entity =>
        {
            entity.ToTable("visits", "sales", table =>
            {
                table.HasCheckConstraint("ck_sales_visit_status", "\"Status\" BETWEEN 1 AND 5");
                table.HasCheckConstraint("ck_sales_visit_slot", "\"EndsAt\" > \"StartsAt\"");
                table.HasCheckConstraint("ck_sales_visit_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_sales_visit_odometer", "\"OdometerOutKm\" IS NULL OR (\"OdometerOutKm\" >= 0 AND (\"OdometerInKm\" IS NULL OR \"OdometerInKm\" >= \"OdometerOutKm\"))");
                table.HasCheckConstraint("ck_sales_visit_incident", "NOT \"IncidentOccurred\" OR \"IncidentComment\" IS NOT NULL");
                table.HasCheckConstraint("ck_sales_visit_test_drive", "\"CheckedOutAt\" IS NULL OR (\"IncludesTestDrive\" AND \"DriverDocumentsChecked\" AND \"IssueChecklist\" IS NOT NULL AND \"OdometerOutKm\" IS NOT NULL AND \"ConditionOut\" IS NOT NULL)");
                table.HasCheckConstraint("ck_sales_visit_check_in", "\"CheckedInAt\" IS NULL OR (\"CheckedOutAt\" IS NOT NULL AND \"ReturnChecklist\" IS NOT NULL AND \"OdometerInKm\" IS NOT NULL AND \"ConditionIn\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_sales_visits_organization_id");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.CreateSignature).HasColumnType("text");
            entity.Property(x => x.IssueChecklist).HasMaxLength(2000);
            entity.Property(x => x.ReturnChecklist).HasMaxLength(2000);
            entity.Property(x => x.ConditionOut).HasMaxLength(2000);
            entity.Property(x => x.ConditionIn).HasMaxLength(2000);
            entity.Property(x => x.IncidentComment).HasMaxLength(2000);
            entity.Property(x => x.Result).HasMaxLength(2000);
            entity.Property(x => x.NextAction).HasMaxLength(1000);
            entity.Property(x => x.ClosureReason).HasMaxLength(2000);
            entity.HasIndex(x => new { x.OrganizationId, x.BranchId, x.StartsAt });
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId, x.StartsAt });
            entity.HasIndex(x => new { x.OrganizationId, x.ResponsibleUserId, x.StartsAt });
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CustomerId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Lead>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeadId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResponsibleUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<VisitHistory>(entity =>
        {
            entity.ToTable("visit_history", "sales");
            entity.HasKey(x => new { x.OrganizationId, x.VisitId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever(); entity.Property(x => x.Operation).HasMaxLength(50);
            entity.Property(x => x.Signature).HasColumnType("text");
            entity.HasIndex(x => new { x.OrganizationId, x.VisitId, x.CommandId }).IsUnique()
                .HasDatabaseName("ux_sales_visit_history_command");
            entity.HasOne<Visit>().WithMany(x => x.History).HasForeignKey(x => new { x.OrganizationId, x.VisitId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOffer>(entity =>
        {
            entity.ToTable("offers", "sales", table =>
            {
                table.HasCheckConstraint("ck_sales_offer_status", "\"Status\" BETWEEN 1 AND 7");
                table.HasCheckConstraint("ck_sales_offer_revision", "\"Revision\" > 0");
                table.HasCheckConstraint("ck_sales_offer_version", "\"Version\" > 0");
                table.HasCheckConstraint("ck_sales_offer_amounts", "\"BasePriceAmount\" > 0 AND \"DiscountAmount\" >= 0 AND \"CostSnapshotAmount\" >= 0 AND \"MinimumMarginAmount\" >= 0");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("ak_sales_offers_organization_id");
            entity.Property(x => x.BasePriceAmount).HasPrecision(19, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(19, 2);
            entity.Property(x => x.CostSnapshotAmount).HasPrecision(19, 2);
            entity.Property(x => x.MinimumMarginAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.CreateSignature).HasColumnType("text");
            entity.Ignore(x => x.LineItemsAmount); entity.Ignore(x => x.FinalPriceAmount);
            entity.Ignore(x => x.ExpectedMarginAmount); entity.Ignore(x => x.IsBelowMinimumMargin);
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId, x.Revision }).IsUnique()
                .HasDatabaseName("ux_sales_offer_lead_revision");
            entity.HasIndex(x => new { x.OrganizationId, x.LeadId }).IsUnique()
                .HasFilter("\"Status\" IN (1, 2, 5)").HasDatabaseName("ux_sales_offer_active");
            entity.HasIndex(x => new { x.OrganizationId, x.BranchId, x.Status });
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CustomerId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Lead>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeadId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SalesOffer>().WithMany().HasForeignKey(x => new { x.OrganizationId, Id = x.RevisesOfferId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.Navigation(x => x.LineItems).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Navigation(x => x.Decisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SalesOfferLineItem>(entity =>
        {
            entity.ToTable("offer_line_items", "sales", table => table.HasCheckConstraint(
                "ck_sales_offer_line_amount", "\"Amount\" > 0"));
            entity.HasKey(x => new { x.OrganizationId, x.OfferId, x.Id }); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Category).HasMaxLength(100); entity.Property(x => x.Name).HasMaxLength(300);
            entity.Property(x => x.Amount).HasPrecision(19, 2); entity.Property(x => x.Currency).HasMaxLength(3);
            entity.HasOne<SalesOffer>().WithMany(x => x.LineItems).HasForeignKey(x => new { x.OrganizationId, x.OfferId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SalesOfferHistory>(entity =>
        {
            entity.ToTable("offer_history", "sales");
            entity.HasKey(x => new { x.OrganizationId, x.OfferId, x.Id }); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Operation).HasMaxLength(50); entity.Property(x => x.Signature).HasColumnType("text");
            entity.HasIndex(x => new { x.OrganizationId, x.OfferId, x.CommandId }).IsUnique()
                .HasDatabaseName("ux_sales_offer_history_command");
            entity.HasOne<SalesOffer>().WithMany(x => x.History).HasForeignKey(x => new { x.OrganizationId, x.OfferId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOfferDecision>(entity =>
        {
            entity.ToTable("offer_decisions", "sales", table => table.HasCheckConstraint(
                "ck_sales_offer_decision", "\"Type\" BETWEEN 1 AND 3"));
            entity.HasKey(x => new { x.OrganizationId, x.OfferId, x.Id }); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Reason).HasMaxLength(2000);
            entity.HasOne<SalesOffer>().WithMany(x => x.Decisions).HasForeignKey(x => new { x.OrganizationId, x.OfferId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovedOfferSnapshot>(entity =>
        {
            entity.ToTable("approved_offer_snapshots", "sales", table => table.HasCheckConstraint(
                "ck_sales_offer_snapshot_amounts", "\"BasePriceAmount\" > 0 AND \"LineItemsAmount\" >= 0 AND \"DiscountAmount\" >= 0 AND \"FinalPriceAmount\" > 0 AND \"CostSnapshotAmount\" >= 0 AND \"MinimumMarginAmount\" >= 0"));
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_sales_offer_snapshots_organization_id");
            entity.HasIndex(x => new { x.OrganizationId, x.OfferId }).IsUnique()
                .HasDatabaseName("ux_sales_offer_snapshot");
            entity.Property(x => x.BasePriceAmount).HasPrecision(19, 2);
            entity.Property(x => x.LineItemsAmount).HasPrecision(19, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(19, 2);
            entity.Property(x => x.FinalPriceAmount).HasPrecision(19, 2);
            entity.Property(x => x.CostSnapshotAmount).HasPrecision(19, 2);
            entity.Property(x => x.ExpectedMarginAmount).HasPrecision(19, 2);
            entity.Property(x => x.MinimumMarginAmount).HasPrecision(19, 2);
            entity.Property(x => x.Currency).HasMaxLength(3); entity.Property(x => x.LineItemsJson).HasColumnType("jsonb");
            entity.HasOne<SalesOffer>().WithOne(x => x.ApprovedSnapshot)
                .HasForeignKey<ApprovedOfferSnapshot>(x => new { x.OrganizationId, x.OfferId })
                .HasPrincipalKey<SalesOffer>(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ApprovedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.ToTable("reservations", "reservations", table =>
            {
                table.HasCheckConstraint("ck_reservations_status", "\"Status\" BETWEEN 1 AND 6");
                table.HasCheckConstraint("ck_reservations_deposit_status", "\"DepositStatus\" BETWEEN 1 AND 6");
                table.HasCheckConstraint("ck_reservations_deposit_amount", "\"DepositAmount\" >= 0");
                table.HasCheckConstraint("ck_reservations_currency", "\"Currency\" ~ '^[A-Z]{3}$'");
                table.HasCheckConstraint("ck_reservations_expiration", "\"ExpiresAt\" > \"StartsAt\"");
                table.HasCheckConstraint("ck_reservations_version", "\"Version\" > 0");
            });
            entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id })
                .HasName("ak_reservations_organization_id");
            entity.Property(x => x.CreateSignature).HasColumnType("text");
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.DepositAmount).HasPrecision(19, 2);
            entity.Property(x => x.DepositReference).HasMaxLength(200);
            entity.Property(x => x.ClosureReason).HasMaxLength(2000);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.CreateCommandId }).IsUnique()
                .HasDatabaseName("ux_reservations_create_command");
            entity.HasIndex(x => new { x.OrganizationId, x.VehicleId }).IsUnique()
                .HasFilter("\"Status\" IN (1, 2)").HasDatabaseName("ux_reservations_active_vehicle");
            entity.HasIndex(x => new { x.OrganizationId, x.BranchId, x.Status, x.ExpiresAt })
                .HasDatabaseName("ix_reservations_queue");
            entity.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BranchId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.VehicleId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CustomerId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Lead>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.LeadId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApprovedOfferSnapshot>().WithMany()
                .HasForeignKey(x => new { x.OrganizationId, x.ApprovedOfferSnapshotId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CreatedByUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ReservationHistory>(entity =>
        {
            entity.ToTable("reservation_history", "reservations");
            entity.HasKey(x => new { x.OrganizationId, x.ReservationId, x.Id });
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Operation).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Signature).HasColumnType("text");
            entity.HasIndex(x => new { x.OrganizationId, x.ReservationId, x.CommandId }).IsUnique()
                .HasDatabaseName("ux_reservation_history_command");
            entity.HasOne<Reservation>().WithMany(x => x.History)
                .HasForeignKey(x => new { x.OrganizationId, x.ReservationId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ActorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
