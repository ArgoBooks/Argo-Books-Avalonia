using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Tests for <see cref="OperationEstimator"/>: the network/compute split, size + load scaling,
/// and the self-calibration of per-machine speed from completed runs.
/// </summary>
public class OperationEstimatorTests
{
    private static TimingPriors PriorsWith(OperationPrior prior) =>
        new() { Model = "test", Priors = [prior] };

    [Fact]
    public void Estimate_WithSeed_ReturnsPositiveAnchors()
    {
        var est = new OperationEstimator();
        var e = est.Estimate(OperationKind.ReceiptScan);
        Assert.True(e.P50Ms > 0);
        Assert.True(e.P90Ms > e.P50Ms);
        Assert.True(e.TotalMs >= e.ComputeMs);
    }

    [Fact]
    public void Estimate_UploadBytes_AddNetworkTime()
    {
        var est = new OperationEstimator { UploadBytesPerMs = 1000 };
        var noUpload = est.Estimate(OperationKind.ReceiptScan);
        var withUpload = est.Estimate(OperationKind.ReceiptScan, uploadBytes: 2_000_000);

        Assert.Equal(0.0, noUpload.NetworkMs, 3);
        Assert.True(withUpload.NetworkMs > 1500); // 2,000,000 bytes / 1000 bytes-per-ms = 2000ms
        Assert.True(withUpload.TotalMs > noUpload.TotalMs);
    }

    [Fact]
    public void Estimate_LoadFactor_ScalesCompute()
    {
        var est = new OperationEstimator();
        var normal = est.Estimate(OperationKind.ReceiptScan).ComputeMs;
        est.UpdateLoadFactor(2.0);
        var busy = est.Estimate(OperationKind.ReceiptScan).ComputeMs;
        Assert.True(busy > normal * 1.8);
    }

    [Fact]
    public void Estimate_SizeCorrection_LargerSizeIsSlower()
    {
        var est = new OperationEstimator(PriorsWith(
            new OperationPrior { Operation = OperationKind.BankCategorize, P50Ms = 6000, P90Ms = 14000, AvgSizeFeature = 50 }));

        var small = est.Estimate(OperationKind.BankCategorize, sizeFeature: 10).ComputeMs;
        var large = est.Estimate(OperationKind.BankCategorize, sizeFeature: 200).ComputeMs;
        Assert.True(large > small);
    }

    [Fact]
    public void Estimate_SizeCorrection_IsClamped()
    {
        var est = new OperationEstimator(PriorsWith(
            new OperationPrior { Operation = OperationKind.BankCategorize, P50Ms = 6000, P90Ms = 14000, AvgSizeFeature = 50 }));

        // 100x the average would blow up the estimate; correction clamps at 2.5x.
        var huge = est.Estimate(OperationKind.BankCategorize, sizeFeature: 5000).ComputeMs;
        Assert.True(huge <= 6000 * 2.5 + 1);
    }

    [Fact]
    public void Estimate_PerPage_ScalesByPageCount()
    {
        var est = new OperationEstimator(PriorsWith(
            new OperationPrior { Operation = OperationKind.BankPdfExtract, P50Ms = 8000, P90Ms = 18000, PerPageMs = 2000 }));

        var onePage = est.Estimate(OperationKind.BankPdfExtract, pageCount: 1).ComputeMs;
        var tenPages = est.Estimate(OperationKind.BankPdfExtract, pageCount: 10).ComputeMs;
        Assert.True(tenPages > onePage * 5);
    }

    [Fact]
    public void RecordResult_CalibratesTowardActual()
    {
        var est = new OperationEstimator(PriorsWith(
            new OperationPrior { Operation = OperationKind.ReceiptScan, P50Ms = 10000, P90Ms = 24000 }));

        Assert.Equal(1.0, est.UserCalibration, 6);
        // This machine consistently runs at ~2x the pooled p50.
        for (int i = 0; i < 30; i++)
            est.RecordResult(OperationKind.ReceiptScan, serverComputeMs: 20000, totalWallClockMs: 20500);

        Assert.True(est.UserCalibration > 1.5);
    }

    [Fact]
    public void RecordResult_LearnsUploadSpeed()
    {
        var est = new OperationEstimator { UploadBytesPerMs = 1500 };
        // 1,000,000 bytes with 2000ms of network time => 500 bytes/ms.
        for (int i = 0; i < 30; i++)
            est.RecordResult(OperationKind.ReceiptScan, serverComputeMs: 8000, totalWallClockMs: 10000, uploadBytes: 1_000_000);

        Assert.InRange(est.UploadBytesPerMs, 450, 560);
    }

    [Fact]
    public void SetPriors_ReplacesAndUpdatesLoadFactor()
    {
        var est = new OperationEstimator();
        est.SetPriors(new TimingPriors
        {
            Model = "x",
            LoadFactor = 1.5,
            Priors = [new OperationPrior { Operation = OperationKind.ReceiptScan, P50Ms = 4000, P90Ms = 9000 }],
        });

        Assert.Equal(1.5, est.CurrentLoadFactor, 6);
        // 4000 p50 * 1.5 load factor = 6000.
        Assert.InRange(est.Estimate(OperationKind.ReceiptScan).ComputeMs, 5900, 6100);
    }
}
