using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StartDX.Dock.Native;

namespace StartDX.Dock.Services;

/// <summary>
/// High-resolution icon loader. Uses IShellItemImageFactory, which returns real 32-bit-alpha bitmaps at up to
/// 256px for classic executables, shortcuts *and* packaged (UWP/MSIX) apps - the GDI icon APIs top out at 32px.
/// All shell calls run on one dedicated STA thread; the frozen results are cached and safe to use from the UI thread.
/// </summary>
internal sealed class IconService : IDisposable
{
    public static IconService Instance { get; } = new();

    private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Thread _thread;

    private IconService()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "StartDX.Icons" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Pump()
    {
        foreach (var work in _queue.GetConsumingEnumerable()) work();
    }

    /// <param name="target">A file path or a shell parsing name ("shell:AppsFolder\...").</param>
    /// <param name="px">Requested edge length in physical pixels (64 for rows, 256 for 128-DIP tiles @2x).</param>
    public Task<ImageSource?> GetAsync(string target, int px) =>
        _cache.GetOrAdd($"{px}|{target}", _ =>
        {
            var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try { tcs.SetResult(Load(target, px)); }
                catch (Exception) { tcs.SetResult(null); }
            });
            return tcs.Task;
        });

    private static ImageSource? Load(string target, int px)
    {
        var iid = ShellGuids.IShellItem;
        try { Shell32.SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out var item); return FromItem(item, px); }
        catch (COMException) { return null; }
    }

    private static ImageSource? FromItem(IShellItem item, int px)
    {
        try
        {
            var factory = (IShellItemImageFactory)item;
            var hr = factory.GetImage(new SIZE(px, px), SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out var hbm);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { Gdi32.DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    /// <summary>Convert an HICON we do NOT own (e.g. from WM_GETICON) into a frozen BitmapSource.</summary>
    public static ImageSource? FromBorrowedIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception) { return null; }
    }

    public void Dispose() => _queue.CompleteAdding();
}
