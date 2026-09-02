using Xunit;

namespace MauriPDF.Rendering.Tests;

public sealed class AssemblyTests
{
    [Fact]
    public void RenderingAssemblyHasExpectedName()
    {
        Assert.Equal("MauriPDF.Rendering", typeof(Rendering.AssemblyMarker).Assembly.GetName().Name);
    }
}
