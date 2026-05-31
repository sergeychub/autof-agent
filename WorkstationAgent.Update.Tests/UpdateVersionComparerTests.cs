using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WorkstationAgent.Update.Tests;

[TestClass]
public sealed class UpdateVersionComparerTests
{
    [TestMethod]
    public void IsUpdateAvailableAcceptsHigherPatchWithBuildMetadata()
    {
        Assert.IsTrue(UpdateVersionComparer.IsUpdateAvailable("0.1.124+abc", "0.1.123+def"));
    }

    [TestMethod]
    public void IsUpdateAvailableRejectsSameVersionWithDifferentBuildMetadata()
    {
        Assert.IsFalse(UpdateVersionComparer.IsUpdateAvailable("0.1.123+abc", "0.1.123+def"));
    }
}
