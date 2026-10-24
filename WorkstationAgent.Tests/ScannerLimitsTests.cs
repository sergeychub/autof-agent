using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkstationAgent.Scanning;

namespace WorkstationAgent.Tests;

[TestClass]
public class ScannerLimitsTests
{
    [TestMethod]
    public void FiveHundredSidesAreAllowedAndTheNextSideStopsTheFeeder()
    {
        using var cancellation = new CancellationTokenSource();
        ScannerLimits.EnsurePageCount(100, cancellation);
        ScannerLimits.EnsurePageCount(500, cancellation);
        Assert.IsFalse(cancellation.IsCancellationRequested);
        var error = Assert.Throws<InvalidOperationException>(() => ScannerLimits.EnsurePageCount(501, cancellation));
        StringAssert.Contains(error.Message, "500");
        Assert.IsTrue(cancellation.IsCancellationRequested);
    }

    [TestMethod]
    public void FileLimitIsInclusiveAndMeasuredInMebibytes()
    {
        ScannerLimits.EnsureFileSize(250L * 1024 * 1024);
        var error = Assert.Throws<InvalidOperationException>(() => ScannerLimits.EnsureFileSize(250L * 1024 * 1024 + 1));
        StringAssert.Contains(error.Message, "250 МиБ");
    }
}
