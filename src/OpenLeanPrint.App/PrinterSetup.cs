using System.Diagnostics;
using System.Drawing.Printing;
using System.Runtime.Versioning;

namespace OpenLeanPrint.App;

/// <summary>
/// Adds and removes the Windows printer queue that feeds the capture service.
/// <para>
/// <c>Add-Printer</c> needs administrator rights, which an app should not have,
/// so this hands the single command to an elevated PowerShell and lets Windows
/// ask the user. That keeps the app unprivileged and makes the elevation
/// visible instead of hidden.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PrinterSetup
{
    /// <summary>The name Windows gives the queue created from our IPP service.</summary>
    // Matches the queue whatever it is called. The product was renamed to
    // Open-LeanPrint, and a machine can still be carrying a queue created under
    // the old name - which the hyphen would otherwise hide.
    private const string NameFragment = "LeanPrint";

    /// <summary>
    /// Whether the Windows capture service is running. When it is, it owns the
    /// IPP port and the app just watches the folder it writes to.
    /// </summary>
    public static bool IsCaptureServiceRunning()
    {
        try
        {
            using var service = new System.ServiceProcess.ServiceController("OpenLeanPrintCapture");
            return service.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch (Exception)
        {
            // Not installed on this machine.
            return false;
        }
    }

    /// <summary>Whether a queue pointing at OpenLeanPrint already exists.</summary>
    public static bool IsRegistered()
    {
        foreach (string? printer in PrinterSettings.InstalledPrinters)
        {
            if (printer is not null &&
                printer.Contains(NameFragment, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Asks Windows to create the printer, elevating via UAC. Returns false if
    /// the user dismissed the prompt.
    /// </summary>
    public static bool Register(int port)
    {
        string url = $"http://localhost:{port}/leanprint";
        // -IppURL creates the port and the queue in one step, using the in-box
        // IPP class driver - no third-party driver is installed.
        return RunElevated($"Add-Printer -IppURL '{url}'");
    }

    /// <summary>Removes every queue whose name mentions OpenLeanPrint.</summary>
    public static bool Unregister() =>
        RunElevated($"Get-Printer | Where-Object Name -like '*{NameFragment}*' | Remove-Printer");

    private static bool RunElevated(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = true, // required for the runas verb
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(startInfo);
            process?.WaitForExit(60_000);
            return process?.ExitCode == 0;
        }
        catch (Exception)
        {
            // The usual case is the user clicking "No" on the UAC prompt.
            return false;
        }
    }
}
