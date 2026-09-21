namespace WritableJsonConfiguration.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Atomic writes are Windows-only.";
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Atomic writes are Windows-only.";
    }
}

public sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Requires a non-Windows runtime.";
    }
}

public class PlatformContractTests
{
    [NonWindowsFact]
    public void ExplicitOptInIsRejectedBeforeAnyWriteOnOtherPlatforms()
    {
        var source = new WritableJsonConfigurationSource { UseAtomicWrites = true };
        Assert.Throws<PlatformNotSupportedException>(() => new WritableJsonConfigurationProvider(source));
        source.UseAtomicWrites = false;
        Assert.NotNull(new WritableJsonConfigurationProvider(source));
    }
}
