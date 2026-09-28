using ArgoBooks.Core.Services;
using ArgoBooks.Shared.Mobile;
using Xunit;

namespace ArgoBooks.Tests.Mobile;

/// <summary>
/// Unit tests for ReceiptScanCoordinator, using a fake IReceiptScannerService so the mobile
/// capture flow's orchestration logic is exercised without a live call to the AI proxy.
/// </summary>
public class ReceiptScanCoordinatorTests
{
    private sealed class FakeScannerService : IReceiptScannerService
    {
        private readonly ReceiptScanResult _result;

        public byte[]? LastImageData;
        public string? LastFileName;
        public int CallCount;

        public FakeScannerService(ReceiptScanResult result)
        {
            _result = result;
        }

        public bool IsConfigured => true;

        public Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastImageData = imageData;
            LastFileName = fileName;
            return Task.FromResult(_result);
        }

        public Task<ReceiptScanResult> ScanReceiptAsync(byte[] imageData, string fileName, bool skipPreprocessing, CancellationToken cancellationToken = default)
            => ScanReceiptAsync(imageData, fileName, cancellationToken);
    }

    [Fact]
    public async Task SuccessfulScan_MapsResultThrough()
    {
        var expected = new ReceiptScanResult { IsSuccess = true, SupplierName = "Acme", TotalAmount = 12.34m };
        var fake = new FakeScannerService(expected);
        var coordinator = new ReceiptScanCoordinator(fake);

        var result = await coordinator.ScanAsync(new byte[] { 1, 2, 3 }, "scan.jpg");

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme", result.SupplierName);
        Assert.Equal(12.34m, result.TotalAmount);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal("scan.jpg", fake.LastFileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, fake.LastImageData);
    }

    [Fact]
    public async Task FailedScan_SurfacesAsFailure()
    {
        var fake = new FakeScannerService(ReceiptScanResult.Failed("Not a valid receipt"));
        var coordinator = new ReceiptScanCoordinator(fake);

        var result = await coordinator.ScanAsync(new byte[] { 1 }, "scan.jpg");

        Assert.False(result.IsSuccess);
        Assert.Equal("Not a valid receipt", result.ErrorMessage);
    }

    [Fact]
    public async Task EmptyImage_ReturnsFailure_WithoutCallingScanner()
    {
        var fake = new FakeScannerService(ReceiptScanResult.Failed("should not be reached"));
        var coordinator = new ReceiptScanCoordinator(fake);

        var result = await coordinator.ScanAsync(Array.Empty<byte>(), "scan.jpg");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task NullImage_ReturnsFailure_WithoutCallingScanner()
    {
        var fake = new FakeScannerService(ReceiptScanResult.Failed("should not be reached"));
        var coordinator = new ReceiptScanCoordinator(fake);

        var result = await coordinator.ScanAsync(null!, "scan.jpg");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public void NullScanner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ReceiptScanCoordinator(null!));
    }
}
