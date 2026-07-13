using DealerOS.Modules.Inspections.Domain;
using DealerOS.SharedKernel;

namespace DealerOS.UnitTests;

public sealed class InspectionDomainTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid InspectorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartAndCancel_UseControlledTransitions()
    {
        var inspection = CreateDraft();
        Assert.Equal(InspectionStatus.Draft, inspection.Status);

        inspection.Start(Now);
        Assert.Equal(InspectionStatus.InProgress, inspection.Status);
        Assert.Equal(2, inspection.Version);

        inspection.Cancel(inspection.Version, Now.AddMinutes(1));
        Assert.Equal(InspectionStatus.Cancelled, inspection.Status);
        Assert.Throws<DomainException>(() => inspection.Start(Now.AddMinutes(2)));
    }

    [Fact]
    public void Complete_RequiresEveryMandatoryChecklistItem()
    {
        var inspection = CreateStarted();
        inspection.SaveItem(inspection.Items.First().Id, InspectionItemResult.Pass, null, inspection.Version, Now);

        var error = Assert.Throws<DomainException>(() => inspection.Complete(null, inspection.Version, Now));
        Assert.Equal("inspection.required_items_incomplete", error.Code);
    }

    [Fact]
    public void CriticalDefect_EnforcesRepairAndSaleBlockOnServer()
    {
        var inspection = CreateStarted();
        var defect = inspection.AddDefect(Guid.NewGuid(), InspectionCategory.Brakes, "Износ диска", "Трещина",
            DefectSeverity.Critical, "Замена", 25_000m, "RUB", false, false, false, false,
            InspectorId, inspection.Version, Now);

        Assert.True(defect.RepairRequired);
        Assert.True(defect.BlocksSale);
        Assert.True(inspection.NeedsReconditioning);
    }

    [Fact]
    public void InvalidEnumValues_AreRejectedBeforePersistence()
    {
        var inspection = CreateStarted();

        Assert.Throws<DomainException>(() => inspection.AddDefect(Guid.NewGuid(), (InspectionCategory)999,
            "Invalid", "Invalid", (DefectSeverity)999, null, null, null, false, false, false, false,
            InspectorId, inspection.Version, Now));
    }

    [Fact]
    public void CompletedInspection_IsImmutableAndCompletionIsIdempotent()
    {
        var inspection = CreateStarted();
        foreach (var item in inspection.Items)
            inspection.SaveItem(item.Id, InspectionItemResult.Pass, null, inspection.Version, Now);
        var versionBefore = inspection.Version;

        Assert.False(inspection.Complete("Готово", versionBefore, Now));
        var completedVersion = inspection.Version;
        Assert.False(inspection.Complete("Повтор", 1, Now.AddMinutes(1)));
        Assert.Equal(completedVersion, inspection.Version);
        Assert.Throws<DomainException>(() => inspection.AddDefect(Guid.NewGuid(), InspectionCategory.Body,
            "Царапина", "Описание", DefectSeverity.Minor, null, null, null, false, false, false, false,
            InspectorId, inspection.Version, Now));
        Assert.Throws<DomainException>(() => inspection.RemoveDraftDefect(Guid.NewGuid(), inspection.Version, Now));
    }

    [Fact]
    public void Correction_CreatesNewRevisionWithoutChangingSource()
    {
        var source = CreateStarted();
        foreach (var item in source.Items)
            source.SaveItem(item.Id, InspectionItemResult.Pass, "checked", source.Version, Now);
        source.Complete("Original", source.Version, Now);

        var correction = Inspection.CreateCorrection(source, InspectorId, Now.AddDays(1));

        Assert.Equal(InspectionStatus.Completed, source.Status);
        Assert.Equal(InspectionStatus.Draft, correction.Status);
        Assert.Equal(source.Id, correction.CorrectsInspectionId);
        Assert.Equal(2, correction.Revision);
        Assert.All(correction.Items, item => Assert.Equal(InspectionItemResult.Pass, item.Result));
    }

    [Fact]
    public void Correction_CopiesPhotoMetadataAndReferencesImmutableSourcePhoto()
    {
        var source = CreateStarted();
        foreach (var item in source.Items)
            source.SaveItem(item.Id, InspectionItemResult.Pass, null, source.Version, Now);
        var defect = source.AddDefect(Guid.NewGuid(), InspectionCategory.Body, "Царапина", "На двери",
            DefectSeverity.Minor, null, null, null, false, false, false, false,
            InspectorId, source.Version, Now);
        var sourcePhoto = source.AddPhoto(defect.Id, Guid.NewGuid(), "evidence.png", "immutable/object.png",
            "image/png", 100, InspectorId, source.Version, Now);
        source.Complete("Original", source.Version, Now);

        var correction = Inspection.CreateCorrection(source, InspectorId, Now.AddDays(1));
        var copiedPhoto = Assert.Single(Assert.Single(correction.Defects).Photos);

        Assert.NotEqual(sourcePhoto.Id, copiedPhoto.Id);
        Assert.Equal(sourcePhoto.Id, copiedPhoto.SourcePhotoId);
        Assert.Equal(sourcePhoto.ObjectKey, copiedPhoto.ObjectKey);
        Assert.Single(Assert.Single(source.Defects).Photos);
        Assert.Equal(InspectionStatus.Completed, source.Status);
    }

    private static Inspection CreateDraft() => Inspection.CreateDraft(OrganizationId, BranchId, VehicleId,
        InspectorId, CreateTemplate(), 42_000, Now);

    private static Inspection CreateStarted()
    {
        var inspection = CreateDraft();
        inspection.Start(Now);
        return inspection;
    }

    private static InspectionTemplate CreateTemplate() => new(Guid.NewGuid(), OrganizationId, "Base", 1,
    [
        new("body", InspectionCategory.Body, "Body", null, true, 1),
        new("brakes", InspectionCategory.Brakes, "Brakes", null, true, 2)
    ], Now, InspectorId);
}
