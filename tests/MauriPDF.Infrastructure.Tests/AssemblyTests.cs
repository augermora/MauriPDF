using Xunit;

namespace MauriPDF.Infrastructure.Tests;

public sealed class AssemblyTests
{
    [Fact]
    public void InfrastructureAssemblyHasExpectedName()
    {
        Assert.Equal("MauriPDF.Infrastructure", typeof(Infrastructure.AssemblyMarker).Assembly.GetName().Name);
    }
}
