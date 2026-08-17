using Xunit;

namespace OpenLeanPrint.Print.Tests;

/// <summary>
/// A fact that only runs on Windows and reports itself as skipped elsewhere, so
/// the suite stays green on Linux/macOS CI while still really exercising GDI+
/// and the print spooler on Windows.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only: uses GDI+ / the Windows print spooler.";
    }
}
