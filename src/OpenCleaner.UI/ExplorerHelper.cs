using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OpenCleaner.UI;

internal static class ExplorerHelper
{
    public static void RevealInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add($"/select,{fullPath}");
                Process.Start(psi)?.Dispose();
                return;
            }

            if (Directory.Exists(fullPath))
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(fullPath);
                Process.Start(psi)?.Dispose();
                return;
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(parent);
                Process.Start(psi)?.Dispose();
            }
        }
        catch
        {
            // Ignore explorer launch errors
        }
    }

    public static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard may be temporarily locked by another application
        }
    }
}
