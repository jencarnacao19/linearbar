using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinearBar.Core.Imaging;
using LinearBar.Infrastructure;
using ManagedShell.Common.Enums;
using ManagedShell.Common.Helpers;
using ManagedShell.UWPInterop;

namespace LinearBar.Shell;

/// <summary>
/// Loads large application icons for the dock.
/// </summary>
/// <remarks>
/// Window icons (what a classic taskbar shows) are 32 px at best — far too small for an icon
/// that is magnified to 70 px and more. The dock therefore asks the shell for the 256 px
/// "jumbo" icon of the app's executable or shortcut, and for the largest tile asset of Store
/// apps. Results are cached per app; loading happens on ManagedShell's icon thread (an STA
/// thread, as the shell image list requires) and the returned images are frozen so they can
/// be used from the UI thread.
/// </remarks>
internal sealed class AppIconLoader
{
    private readonly ConcurrentDictionary<string, Task<AppIcon?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon of an executable, shortcut or any other file-system item.</summary>
    public Task<AppIcon?> LoadFileAsync(string path)
    {
        return _cache.GetOrAdd("file:" + path, _ => RunOnIconThread(() => LoadFile(path)));
    }

    /// <summary>Icon and display name of a Store/UWP app.</summary>
    public Task<AppIcon?> LoadStoreAppAsync(string aumid)
    {
        return _cache.GetOrAdd("aumid:" + aumid, _ => RunOnIconThread(() => LoadStoreApp(aumid)));
    }

    private static Task<AppIcon?> RunOnIconThread(Func<AppIcon?> load)
    {
        return Task.Factory.StartNew(
            () =>
            {
                try
                {
                    return load();
                }
                catch (Exception ex)
                {
                    Log.Warn("Icon load failed: " + ex.Message);
                    return null;
                }
            },
            CancellationToken.None,
            TaskCreationOptions.None,
            IconHelper.IconScheduler);
    }

    private static AppIcon? LoadFile(string path)
    {
        if (!ShellHelper.Exists(path))
        {
            return null;
        }

        IntPtr hIcon = IntPtr.Zero;

        // Ask by item ID list: that makes the shell look at the actual file, so executables
        // and shortcuts yield their own icon instead of the generic one for their extension.
        IntPtr pidl = Native.ILCreateFromPathW(path);
        if (pidl != IntPtr.Zero)
        {
            try
            {
                hIcon = IconHelper.GetIconByPidl(pidl, IconSize.Jumbo);
            }
            finally
            {
                Native.ILFree(pidl);
            }
        }

        if (hIcon == IntPtr.Zero)
        {
            hIcon = IconHelper.GetIconByFilename(path, IconSize.Jumbo);
        }

        // GetImageFromHIcon destroys the icon handle and freezes the result.
        BitmapSource? image = hIcon != IntPtr.Zero && IconImageConverter.GetImageFromHIcon(hIcon, returnDefault: false) is BitmapSource bitmap
            ? TrimPadding(bitmap)
            : null;

        return new AppIcon(image, DescribeFile(path), DominantColour(image));
    }

    /// <summary>Friendly name of a file: the product description of executables, else the file name.</summary>
    private static string? DescribeFile(string path)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    return description.Trim();
                }
            }

            string name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static AppIcon? LoadStoreApp(string aumid)
    {
        StoreApp? app = StoreAppHelper.AppList.GetAppByAumid(aumid);
        if (app is null)
        {
            return null;
        }

        ImageSource? image = app.GetIconImageSource(IconSize.Jumbo);
        if (image is { IsFrozen: false, CanFreeze: true })
        {
            image.Freeze();
        }

        return new AppIcon(image, string.IsNullOrWhiteSpace(app.DisplayName) ? null : app.DisplayName, DominantColour(image as BitmapSource));
    }

    /// <summary>The icon's characteristic colour, used to tint its tile; null for grey icons.</summary>
    internal static Color? DominantColour(BitmapSource? source)
    {
        if (source is null)
        {
            return null;
        }

        try
        {
            BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            return IconPalette.Dominant(pixels, width, height, stride) is { } rgb
                ? Color.FromRgb(rgb.R, rgb.G, rgb.B)
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or System.IO.IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Crops a jumbo bitmap that is mostly transparent padding around a small icon (what the
    /// shell returns for executables without a large icon) down to the icon itself.
    /// </summary>
    internal static BitmapSource TrimPadding(BitmapSource source)
    {
        try
        {
            BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            var content = AlphaBounds.Find(pixels, width, height, stride);
            if (!AlphaBounds.TryGetCrop(content, width, height, out var crop))
            {
                return source;
            }

            var cropped = new CroppedBitmap(bgra, new Int32Rect(crop.X, crop.Y, crop.Size, crop.Size));
            cropped.Freeze();
            return cropped;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return source;
        }
    }
}

/// <summary>Result of an icon lookup.</summary>
/// <param name="Image">The icon, or null when none could be loaded.</param>
/// <param name="DisplayName">App name found along the way, or null.</param>
/// <param name="Tint">The icon's dominant colour, or null for colourless icons.</param>
internal sealed record AppIcon(ImageSource? Image, string? DisplayName, Color? Tint);
