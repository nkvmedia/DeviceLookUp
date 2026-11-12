using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using DeviceLookup.Core.Services;
using Microsoft.Win32;

namespace DeviceLookup.Services;

public sealed class WpfPlatform : IPlatform
{
    public string? PickOpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, Title = "Import CSV" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string defaultName, string filter)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName, Title = "Export CSV" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public void SetClipboard(string text)
    {
        // The clipboard can be briefly locked by another app; retry a few times.
        for (var i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(40); }
        }
    }

    public void OpenPath(string pathOrUrl)
    {
        try { Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true }); }
        catch (Exception ex) { AppLogger.Error($"Couldn't open {pathOrUrl}", ex); }
    }
}

/// <summary>Encrypts the Dell secret with Windows DPAPI (tied to the signed-in Windows user).</summary>
public sealed class DpapiProtector : ISecretProtector
{
    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public string Unprotect(string protectedText) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser));
}
