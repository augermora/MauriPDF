using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class AssemblyTests
{
    [Fact]
    public void CoreAssemblyHasExpectedName()
    {
        Assert.Equal("MauriPDF.Core", typeof(Core.AssemblyMarker).Assembly.GetName().Name);
    }
}
