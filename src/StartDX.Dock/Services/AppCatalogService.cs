using System.Diagnostics;
using System.Runtime.InteropServices;
using StartDX.Dock.Infrastructure;
using StartDX.Dock.Native;
using StartDX.Dock.ViewModels;

namespace StartDX.Dock.Services;

/// <summary>
/// Builds the "All apps" list by enumerating the virtual shell:AppsFolder - the same source the stock Start menu uses.
/// It contains classic Win32 shortcuts *and* packaged apps, and every child can be launched with
/// ShellExecute("shell:AppsFolder\{parsing name}"). Falls back to scanning Start Menu .lnk files if the shell folder
/// cannot be enumerated.
/// </summary>
internal static class AppCatalogService
{
    private const string AppsFolderPrefix = @"shell:AppsFolder\";

    public static Task<IReadOnlyList<AppEntry>> LoadAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<AppEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var apps = EnumerateAppsFolder();
                if (apps.Count == 0) apps = ScanStartMenuFolders();
                tcs.SetResult(apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            }
            catch (Exception ex)
            {
                Log.Error("App catalog load failed", ex);
                tcs.SetResult(Array.Empty<AppEntry>());
            }
        }) { IsBackground = true, Name = "StartDX.AppCatalog" };
        thread.SetApartmentState(ApartmentState.STA);   // shell COM objects are STA
        thread.Start();
        return tcs.Task;
    }

    private static List<AppEntry> EnumerateAppsFolder()
    {
        var result = new List<AppEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var iid = ShellGuids.IShellItem;
        Shell32.SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref iid, out var folder);
        try
        {
            var bhid = ShellGuids.BHID_EnumItems;
            var ienum = ShellGuids.IEnumShellItems;
            if (folder.BindToHandler(IntPtr.Zero, ref bhid, ref ienum, out var ppv) != 0 || ppv == IntPtr.Zero) return result;

            var enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(ppv);
            Marshal.Release(ppv);
            try
            {
                while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        if (item.GetDisplayName(SIGDN.NORMALDISPLAY, out var name) != 0 || string.IsNullOrWhiteSpace(name)) continue;
                        if (item.GetDisplayName(SIGDN.PARENTRELATIVEPARSING, out var id) != 0 || string.IsNullOrWhiteSpace(id)) continue;
                        if (name.StartsWith("Uninstall ", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!seen.Add(id)) continue;

                        result.Add(new AppEntry { Name = name, Target = AppsFolderPrefix + id });
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }
        finally { Marshal.ReleaseComObject(folder); }
        return result;
    }

    private static List<AppEntry> ScanStartMenuFolders()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };

        var byName = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                byName.TryAdd(name, new AppEntry { Name = name, Target = file });
            }
        }
        return byName.Values.ToList();
    }
}

/// <summary>Launching helper: anything ShellExecute understands (paths, URLs, shell:AppsFolder\..., commands).</summary>
internal static class ShellLauncher
{
    public static bool Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Launch failed for '{target}': {ex.Message}");
            return false;
        }
    }
}
