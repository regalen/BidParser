using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BidParser.Desktop.ViewModels;

namespace BidParser.Desktop;

public partial class MainWindow : Window
{
    /// <summary>
    /// The size the line pricing review needs to show all twelve columns without crowding. The
    /// window grows towards it when the review opens; it never shrinks, and never exceeds the work
    /// area of the monitor the window is on.
    /// </summary>
    private const double PricingReviewWidth = 1320;
    private const double PricingReviewHeight = 820;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Moves keyboard focus into the overwrite overlay when it opens, so Tab and Space act on the
    /// dialog rather than on the window behind the scrim, and makes room for the line pricing
    /// review when it opens. Binding cannot express either.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasPricingReview)
            && sender is MainWindowViewModel { HasPricingReview: true })
        {
            GrowForPricingReview();
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.HasDialog)
            || sender is not MainWindowViewModel { HasDialog: true })
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
            DialogHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    private void GrowForPricingReview()
    {
        if (WindowState != WindowState.Normal || TryGetWorkArea() is not { } workArea)
        {
            return;
        }

        var width = Math.Min(Math.Max(ActualWidth, PricingReviewWidth), workArea.Width);
        var height = Math.Min(Math.Max(ActualHeight, PricingReviewHeight), workArea.Height);
        if (width <= ActualWidth && height <= ActualHeight)
        {
            return;
        }

        // Grow around the current centre, then keep the whole window on the same monitor.
        var left = Left - (width - ActualWidth) / 2;
        var top = Top - (height - ActualHeight) / 2;
        Left = Math.Clamp(left, workArea.Left, workArea.Right - width);
        Top = Math.Clamp(top, workArea.Top, workArea.Bottom - height);
        Width = width;
        Height = height;
    }

    /// <summary>The work area of the window's monitor, in device-independent pixels.</summary>
    private Rect? TryGetWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            info.WorkArea.Left / dpi.DpiScaleX,
            info.WorkArea.Top / dpi.DpiScaleY,
            (info.WorkArea.Right - info.WorkArea.Left) / dpi.DpiScaleX,
            (info.WorkArea.Bottom - info.WorkArea.Top) / dpi.DpiScaleY);
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
