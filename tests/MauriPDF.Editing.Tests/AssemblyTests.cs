using Xunit;

namespace MauriPDF.Editing.Tests;

public sealed class AssemblyTests
{
    [Fact]
    public void EditingAssemblyHasExpectedName()
    {
        Assert.Equal("MauriPDF.Editing", typeof(Editing.AssemblyMarker).Assembly.GetName().Name);
    }
}
