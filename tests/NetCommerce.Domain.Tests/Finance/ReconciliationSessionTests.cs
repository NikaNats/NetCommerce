using NetCommerce.Finance.Domain.Reconciliation;

namespace NetCommerce.Domain.Tests.Finance;

/// <summary>
///     Unit tests for ReconciliationSession aggregate.
///     Tests domain logic for session creation, discrepancy tracking, and state transitions.
/// </summary>
public class ReconciliationSessionTests
{
    [Fact]
    public void Create_ShouldInitializeSessionWithStartedStatus()
    {
        // Arrange
        var date = new DateTime(2026, 1, 5);

        // Act
        var session = ReconciliationSession.Create(date);

        // Assert
        session.CalculatedForDate.ShouldBe(date.Date);
        session.Status.ShouldBe(ReconciliationStatus.Started);
        session.StartedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(-5));
        session.CompletedAt.ShouldBeNull();
        session.Discrepancies.ShouldBeEmpty();
        session.TotalInternalAmount.ShouldBe(0);
        session.TotalExternalAmount.ShouldBe(0);
    }

    [Fact]
    public void SetTotals_ShouldUpdateInternalAndExternalAmounts()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);

        // Act
        session.SetTotals(1000.00m, 995.00m);

        // Assert
        session.TotalInternalAmount.ShouldBe(1000.00m);
        session.TotalExternalAmount.ShouldBe(995.00m);
    }

    [Fact]
    public void AddDiscrepancy_ShouldAddToDiscrepancyList()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);
        var discrepancy = new Discrepancy(
            "ch_test_123",
            DiscrepancyType.MissingInternal,
            99.99m,
            "CRITICAL: Customer charged but no order exists");

        // Act
        session.AddDiscrepancy(discrepancy);

        // Assert
        session.Discrepancies.ShouldHaveSingleItem();
        session.Discrepancies.First().ExternalTxnId.ShouldBe("ch_test_123");
        session.Discrepancies.First().Type.ShouldBe(DiscrepancyType.MissingInternal);
        session.Discrepancies.First().Difference.ShouldBe(99.99m);
    }

    [Fact]
    public void AddDiscrepancy_MultipleDiscrepancies_ShouldTrackAll()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);
        var discrepancy1 = new Discrepancy("ch_1", DiscrepancyType.MissingInternal, 100m, "Ghost charge");
        var discrepancy2 = new Discrepancy("ch_2", DiscrepancyType.AmountMismatch, 0.50m, "Rounding error");
        var discrepancy3 = new Discrepancy("ch_3", DiscrepancyType.MissingExternal, 50m, "Internal error");

        // Act
        session.AddDiscrepancy(discrepancy1);
        session.AddDiscrepancy(discrepancy2);
        session.AddDiscrepancy(discrepancy3);

        // Assert
        session.Discrepancies.Count.ShouldBe(3);
        session.Discrepancies.Count(d => d.Type == DiscrepancyType.MissingInternal).ShouldBe(1);
        session.Discrepancies.Count(d => d.Type == DiscrepancyType.AmountMismatch).ShouldBe(1);
        session.Discrepancies.Count(d => d.Type == DiscrepancyType.MissingExternal).ShouldBe(1);
    }

    [Fact]
    public void MarkAsCompleted_NoDiscrepancies_ShouldSetStatusToMatched()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);
        session.SetTotals(1000m, 1000m);

        // Act
        session.MarkAsCompleted();

        // Assert
        session.Status.ShouldBe(ReconciliationStatus.Matched);
        session.CompletedAt.ShouldNotBeNull();
        session.CompletedAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void MarkAsCompleted_WithDiscrepancies_ShouldSetStatusToMismatched()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);
        session.SetTotals(1000m, 999m);
        session.AddDiscrepancy(new Discrepancy("ch_1", DiscrepancyType.AmountMismatch, 1m, "Small difference"));

        // Act
        session.MarkAsCompleted();

        // Assert
        session.Status.ShouldBe(ReconciliationStatus.Mismatched);
        session.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public void MarkAsFailed_ShouldSetStatusToFailedWithNote()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);
        var errorMessage = "PSP API connection timeout";

        // Act
        session.MarkAsFailed(errorMessage);

        // Assert
        session.Status.ShouldBe(ReconciliationStatus.Failed);
        session.CompletedAt.ShouldNotBeNull();
        session.Notes.ShouldContain(errorMessage);
    }

    [Fact]
    public void AddNote_ShouldAppendToExistingNotes()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);

        // Act
        session.AddNote("First note");
        session.AddNote("Second note");

        // Assert
        session.Notes.ShouldContain("First note");
        session.Notes.ShouldContain("Second note");
    }

    [Theory]
    [InlineData(DiscrepancyType.MissingInternal)]
    [InlineData(DiscrepancyType.MissingExternal)]
    [InlineData(DiscrepancyType.AmountMismatch)]
    public void Discrepancy_AllTypes_ShouldBeCreatedCorrectly(DiscrepancyType type)
    {
        // Arrange & Act
        var discrepancy = new Discrepancy("ch_test", type, 100m, $"Test for {type}");

        // Assert
        discrepancy.Type.ShouldBe(type);
        discrepancy.ExternalTxnId.ShouldBe("ch_test");
        discrepancy.Difference.ShouldBe(100m);
        discrepancy.DetectedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void ReconciliationStatus_AllValues_ShouldBeAvailable()
    {
        // Verify all enum values exist
        Enum.GetValues<ReconciliationStatus>().ShouldContain(ReconciliationStatus.Started);
        Enum.GetValues<ReconciliationStatus>().ShouldContain(ReconciliationStatus.Matched);
        Enum.GetValues<ReconciliationStatus>().ShouldContain(ReconciliationStatus.Mismatched);
        Enum.GetValues<ReconciliationStatus>().ShouldContain(ReconciliationStatus.Failed);
    }

    [Fact]
    public void DiscrepancyType_AllValues_ShouldBeAvailable()
    {
        // Verify all enum values exist
        Enum.GetValues<DiscrepancyType>().ShouldContain(DiscrepancyType.MissingInternal);
        Enum.GetValues<DiscrepancyType>().ShouldContain(DiscrepancyType.MissingExternal);
        Enum.GetValues<DiscrepancyType>().ShouldContain(DiscrepancyType.AmountMismatch);
    }

    [Fact]
    public void Create_DateWithTime_ShouldNormalizeToDayStart()
    {
        // Arrange
        var dateWithTime = new DateTime(2026, 1, 5, 14, 30, 45);

        // Act
        var session = ReconciliationSession.Create(dateWithTime);

        // Assert
        session.CalculatedForDate.ShouldBe(new DateTime(2026, 1, 5, 0, 0, 0));
        session.CalculatedForDate.TimeOfDay.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Session_WithGhostCharges_ShouldBeIdentifiable()
    {
        // Arrange
        var session = ReconciliationSession.Create(DateTime.Today);
        session.AddDiscrepancy(new Discrepancy("ch_ghost_1", DiscrepancyType.MissingInternal, 100m, "Ghost charge"));
        session.AddDiscrepancy(new Discrepancy("ch_ghost_2", DiscrepancyType.MissingInternal, 50m, "Ghost charge"));
        session.MarkAsCompleted();

        // Act
        var ghostCharges = session.Discrepancies.Where(d => d.Type == DiscrepancyType.MissingInternal).ToList();

        // Assert
        ghostCharges.Count.ShouldBe(2);
        ghostCharges.Sum(d => d.Difference).ShouldBe(150m);
        session.Status.ShouldBe(ReconciliationStatus.Mismatched);
    }
}
