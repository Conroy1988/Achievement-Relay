using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using SystemColors = System.Windows.SystemColors;

namespace AchievementRelay.App;

public partial class AchievementOverlayWindow : Window
{
    public const int OverlayWidth = 520;
    public const int OverlayHeight = 76;
    public static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);

    private const int ExtendedWindowStyleIndex = -20;
    private const long ExtendedStyleToolWindow = 0x00000080L;
    private const long ExtendedStyleTransparent = 0x00000020L;
    private const long ExtendedStyleNoActivate = 0x08000000L;
    private const int WindowMessageNonClientHitTest = 0x0084;
    private const int WindowMessageMouseActivate = 0x0021;
    private const int WindowMessageSettingChange = 0x001A;
    private const int WindowMessageDisplayChange = 0x007E;
    private const int WindowMessageDpiChanged = 0x02E0;
    private const int HitTestTransparent = -1;
    private const int MouseActivateNoActivate = 3;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowPositionNoActivate = 0x0010;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionNoOwnerZOrder = 0x0200;
    private const uint SetWindowPositionShowWindow = 0x0040;
    private const uint SetWindowPositionFrameChanged = 0x0020;
    private const int TopInsetDips = 16;
    private static readonly IntPtr TopmostWindow = new(-1);

    private readonly AchievementOverlayPresentation _presentation;
    private readonly TaskCompletionSource _motionSuppressed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr _windowHandle;
    private IntPtr _foregroundWindow;
    private HwndSource? _source;

    public AchievementOverlayWindow(AchievementOverlayPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        InitializeComponent();
        _presentation = presentation;
        AutomationProperties.SetName(this, presentation.AccessibleAnnouncement);
        ApplyPresentation();
    }

    public async Task ShowForAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _foregroundWindow = GetForegroundWindow();
        var useMotion = SystemParameters.ClientAreaAnimation &&
                        SystemParameters.UIEffects &&
                        !SystemParameters.HighContrast;

        if (useMotion)
        {
            Opacity = 0;
            OverlayTranslate.Y = -OverlayHeight;
        }
        else
        {
            Opacity = 1;
            OverlayTranslate.Y = 0;
        }

        try
        {
            Show();
            UpdateLayout();
            PositionOnForegroundMonitor();
            RaiseAccessibleAnnouncement();

            if (useMotion)
            {
                await Task.WhenAll(
                    AnimateAsync(this, OpacityProperty, 1, TimeSpan.FromMilliseconds(240), EasingMode.EaseOut, _motionSuppressed.Task),
                    AnimateAsync(OverlayTranslate, TranslateTransform.YProperty, 0, TimeSpan.FromMilliseconds(240), EasingMode.EaseOut, _motionSuppressed.Task));
            }

            await Task.Delay(DisplayDuration, cancellationToken);

            if (useMotion)
            {
                await Task.WhenAll(
                    AnimateAsync(this, OpacityProperty, 0, TimeSpan.FromMilliseconds(180), EasingMode.EaseIn, _motionSuppressed.Task),
                    AnimateAsync(OverlayTranslate, TranslateTransform.YProperty, -OverlayHeight, TimeSpan.FromMilliseconds(180), EasingMode.EaseIn, _motionSuppressed.Task));
            }
        }
        finally
        {
            if (IsVisible || _source is not null)
            {
                Close();
            }
        }
    }

    public static byte[] RenderPreview(AchievementOverlayPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var window = new AchievementOverlayWindow(presentation)
        {
            Opacity = 1
        };
        window.OverlayTranslate.Y = 0;
        window.OverlayRoot.Measure(new Size(OverlayWidth, OverlayHeight));
        window.OverlayRoot.Arrange(new Rect(0, 0, OverlayWidth, OverlayHeight));
        window.OverlayRoot.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            OverlayWidth,
            OverlayHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(window.OverlayRoot);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPointer(_windowHandle, ExtendedWindowStyleIndex).ToInt64();
        extendedStyle |= ExtendedStyleToolWindow | ExtendedStyleTransparent | ExtendedStyleNoActivate;
        SetWindowLongPointer(_windowHandle, ExtendedWindowStyleIndex, new IntPtr(extendedStyle));
        SetWindowPos(
            _windowHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SetWindowPositionNoActivate |
            SetWindowPositionNoMove |
            SetWindowPositionNoSize |
            SetWindowPositionNoZOrder |
            SetWindowPositionFrameChanged);

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowProcedure);
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        PositionOnForegroundMonitor();
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new AchievementOverlayAutomationPeer(this);

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        base.OnClosed(e);
    }

    private void ApplyPresentation(bool refreshArtwork = true)
    {
        EyebrowText.Text = _presentation.Eyebrow;
        AchievementNameText.Text = _presentation.AchievementName;
        GameAndRewardText.Text = _presentation.GameAndReward;
        RarityPercentageText.Text = _presentation.Percentage;
        TierNameText.Text = $"{_presentation.TierName} tier";

        var visual = TierVisual.For(_presentation.Tier);
        ApplyStandardTheme(visual);
        TierGlyph.Data = Geometry.Parse(visual.Geometry);
        TierGlyphMark.Text = visual.Mark;

        if (refreshArtwork)
        {
            var imageSource = TryCreateArtwork(_presentation.AchievementIconBytes);
            if (imageSource is not null)
            {
                AchievementArtwork.Source = imageSource;
                AchievementArtwork.Visibility = Visibility.Visible;
                FallbackArtwork.Visibility = Visibility.Collapsed;
            }
            else
            {
                AchievementArtwork.Source = null;
                AchievementArtwork.Visibility = Visibility.Collapsed;
                FallbackArtwork.Visibility = Visibility.Visible;
            }
        }

        if (SystemParameters.HighContrast)
        {
            ApplyHighContrastTheme();
        }
    }

    private void ApplyStandardTheme(TierVisual visual)
    {
        OverlayFrame.Background = new SolidColorBrush(Color.FromArgb(250, 11, 13, 16));
        OverlayFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(115, 123, 126));
        OverlayFrame.BorderThickness = new Thickness(1);
        OverlayFrame.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 11,
            ShadowDepth = 3,
            Opacity = 0.58,
            Color = Colors.Black
        };
        SignalLine.Fill = new SolidColorBrush(Color.FromRgb(215, 43, 50));
        ArtworkFrame.Background = new SolidColorBrush(Color.FromRgb(23, 28, 32));
        ArtworkFrame.BorderBrush = new SolidColorBrush(Color.FromRgb(101, 110, 114));
        FallbackArtwork.Background = new SolidColorBrush(Color.FromRgb(17, 21, 25));
        EyebrowText.Foreground = new SolidColorBrush(Color.FromRgb(255, 114, 120));
        AchievementNameText.Foreground = new SolidColorBrush(Color.FromRgb(247, 244, 238));
        GameAndRewardText.Foreground = new SolidColorBrush(Color.FromRgb(192, 198, 201));
        TierGlyph.Fill = new SolidColorBrush(visual.Light);
        TierGlyphMark.Foreground = new SolidColorBrush(visual.Dark);
        RarityPercentageText.Foreground = new SolidColorBrush(visual.Light);
        TierNameText.Foreground = new SolidColorBrush(visual.Light);
        RarityPanel.Background = new SolidColorBrush(Color.FromArgb(44, visual.Mid.R, visual.Mid.G, visual.Mid.B));
        RarityPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(118, visual.Mid.R, visual.Mid.G, visual.Mid.B));
    }

    private void ApplyHighContrastTheme()
    {
        OverlayFrame.Background = SystemColors.WindowBrush;
        OverlayFrame.BorderBrush = SystemColors.HighlightBrush;
        OverlayFrame.BorderThickness = new Thickness(2);
        OverlayFrame.Effect = null;
        SignalLine.Fill = SystemColors.HighlightBrush;
        ArtworkFrame.Background = SystemColors.WindowBrush;
        ArtworkFrame.BorderBrush = SystemColors.WindowTextBrush;
        FallbackArtwork.Background = SystemColors.WindowBrush;
        EyebrowText.Foreground = SystemColors.WindowTextBrush;
        AchievementNameText.Foreground = SystemColors.WindowTextBrush;
        GameAndRewardText.Foreground = SystemColors.WindowTextBrush;
        RarityPanel.Background = SystemColors.WindowBrush;
        RarityPanel.BorderBrush = SystemColors.HighlightBrush;
        TierGlyph.Fill = SystemColors.HighlightBrush;
        TierGlyphMark.Foreground = SystemColors.HighlightTextBrush;
        RarityPercentageText.Foreground = SystemColors.WindowTextBrush;
        TierNameText.Foreground = SystemColors.WindowTextBrush;
    }

    private void RaiseAccessibleAnnouncement()
    {
        try
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(this) ?? new WindowAutomationPeer(this);
            peer.RaiseNotificationEvent(
                AutomationNotificationKind.ItemAdded,
                AutomationNotificationProcessing.MostRecent,
                _presentation.AccessibleAnnouncement,
                "AchievementRelay.SignalStrip");
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or
                                          ExternalException or
                                          InvalidOperationException or
                                          NotSupportedException)
        {
            // Accessibility notification support is optional on older or
            // partially initialized Windows automation stacks.
        }
    }

    private void PositionOnForegroundMonitor()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var referenceWindow = _foregroundWindow != IntPtr.Zero
            ? _foregroundWindow
            : _windowHandle;
        var monitor = MonitorFromWindow(referenceWindow, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var workWidth = monitorInfo.WorkArea.Width;
        var workHeight = monitorInfo.WorkArea.Height;
        if (workWidth <= 0 || workHeight <= 0)
        {
            return;
        }

        // Move the PMv2 overlay HWND onto the target monitor before querying
        // its DPI. A foreground game can itself be DPI-unaware and report 96
        // even on a scaled display, so its window DPI is not a safe sizing
        // source for this overlay.
        var provisionalDpi = GetDpiForWindow(_windowHandle);
        provisionalDpi = provisionalDpi == 0 ? 96u : provisionalDpi;
        var provisionalScale = provisionalDpi / 96d;
        var provisionalWidth = Math.Min(
            workWidth,
            Math.Max(1, (int)Math.Round(OverlayWidth * provisionalScale)));
        var provisionalHeight = Math.Min(
            workHeight,
            Math.Max(1, (int)Math.Round(OverlayHeight * provisionalScale)));
        var provisionalLeft = Math.Clamp(
            monitorInfo.WorkArea.Left + (workWidth - provisionalWidth) / 2,
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Right - provisionalWidth);
        var provisionalTop = Math.Clamp(
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Bottom - provisionalHeight);
        SetWindowPos(
            _windowHandle,
            TopmostWindow,
            provisionalLeft,
            provisionalTop,
            provisionalWidth,
            provisionalHeight,
            SetWindowPositionNoActivate | SetWindowPositionNoOwnerZOrder);

        var dpi = GetDpiForWindow(_windowHandle);
        dpi = dpi == 0 ? 96u : dpi;
        var scale = dpi / 96d;
        var widthPixels = Math.Min(workWidth, Math.Max(1, (int)Math.Round(OverlayWidth * scale)));
        var heightPixels = Math.Min(workHeight, Math.Max(1, (int)Math.Round(OverlayHeight * scale)));
        var left = Math.Clamp(
            monitorInfo.WorkArea.Left + (workWidth - widthPixels) / 2,
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Right - widthPixels);
        var top = Math.Clamp(
            monitorInfo.WorkArea.Top + (int)Math.Round(TopInsetDips * scale),
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Bottom - heightPixels);

        SetWindowPos(
            _windowHandle,
            TopmostWindow,
            left,
            top,
            widthPixels,
            heightPixels,
            SetWindowPositionNoActivate |
            SetWindowPositionNoOwnerZOrder |
            SetWindowPositionShowWindow);
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) &&
            e.PropertyName is not nameof(SystemParameters.ClientAreaAnimation) and
                not nameof(SystemParameters.UIEffects) and
                not nameof(SystemParameters.HighContrast))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                Dispatcher.BeginInvoke(new Action(() => OnSystemParameterChanged(sender, e)));
            }
            catch (InvalidOperationException)
            {
                // A preference event can race dispatcher shutdown.
            }

            return;
        }

        try
        {
            // Artwork does not change with a Windows accessibility preference,
            // so avoid decoding provider bytes again from a static event.
            ApplyPresentation(refreshArtwork: false);
            if (!SystemParameters.ClientAreaAnimation ||
                !SystemParameters.UIEffects ||
                SystemParameters.HighContrast)
            {
                _motionSuppressed.TrySetResult();
            }
        }
        catch (Exception)
        {
            // A cosmetic live-theme refresh must never reach the application's
            // global exception path or affect achievement delivery.
        }
    }

    private IntPtr WindowProcedure(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        switch (message)
        {
            case WindowMessageMouseActivate:
                handled = true;
                return new IntPtr(MouseActivateNoActivate);
            case WindowMessageNonClientHitTest:
                handled = true;
                return new IntPtr(HitTestTransparent);
            case WindowMessageSettingChange:
            case WindowMessageDisplayChange:
            case WindowMessageDpiChanged:
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(PositionOnForegroundMonitor));
                    }
                    catch (InvalidOperationException)
                    {
                        // Display changes can race window/dispatcher teardown.
                    }
                }

                break;
        }

        return IntPtr.Zero;
    }

    private static async Task AnimateAsync(
        DependencyObject target,
        DependencyProperty property,
        double destination,
        TimeSpan duration,
        EasingMode easingMode,
        Task motionSuppressed)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new DoubleAnimation
        {
            To = destination,
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new CubicEase { EasingMode = easingMode }
        };
        animation.Completed += (_, _) => completion.TrySetResult();
        ApplyAnimation(target, property, animation);
        var finished = await Task.WhenAny(completion.Task, motionSuppressed);
        if (ReferenceEquals(finished, motionSuppressed))
        {
            ApplyAnimation(target, property, null);
            target.SetValue(property, destination);
        }
    }

    private static void ApplyAnimation(
        DependencyObject target,
        DependencyProperty property,
        AnimationTimeline? animation)
    {
        switch (target)
        {
            case Animatable animatable:
                animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            case UIElement element:
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                break;
            default:
                throw new NotSupportedException($"{target.GetType().Name} does not support WPF animations.");
        }
    }

    private static ImageSource? TryCreateArtwork(byte[]? bytes)
    {
        const int maximumDimension = 8192;
        const long maximumPixels = 64L * 1024 * 1024;
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using (var metadataStream = new MemoryStream(bytes, writable: false))
            {
                var decoder = BitmapDecoder.Create(
                    metadataStream,
                    BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                if (frame.PixelWidth is <= 0 or > maximumDimension ||
                    frame.PixelHeight is <= 0 or > maximumDimension ||
                    (long)frame.PixelWidth * frame.PixelHeight > maximumPixels)
                {
                    return null;
                }
            }

            using var imageStream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = 128;
            image.StreamSource = imageStream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          FileFormatException or
                                          InvalidOperationException or
                                          NotSupportedException)
        {
            return null;
        }
    }

    private static IntPtr GetWindowLongPointer(IntPtr windowHandle, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new IntPtr(GetWindowLong32(windowHandle, index));

    private static IntPtr SetWindowLongPointer(IntPtr windowHandle, int index, IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new IntPtr(SetWindowLong32(windowHandle, index, value.ToInt32()));

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr windowHandle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRectangle MonitorArea;
        public NativeRectangle WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    private sealed record TierVisual(Color Dark, Color Mid, Color Light, string Geometry, string Mark)
    {
        public static TierVisual For(RelayRarityTier tier) => tier switch
        {
            RelayRarityTier.Bronze => new TierVisual(
                Color.FromRgb(92, 43, 21),
                Color.FromRgb(184, 106, 58),
                Color.FromRgb(242, 177, 111),
                "M12,0 L18,2 L22,6 L24,12 L22,18 L18,22 L12,24 L6,22 L2,18 L0,12 L2,6 L6,2 Z",
                "B"),
            RelayRarityTier.Silver => new TierVisual(
                Color.FromRgb(65, 73, 80),
                Color.FromRgb(162, 173, 181),
                Color.FromRgb(239, 245, 248),
                "M12,1 L22,5 L20,16 L12,23 L4,16 L2,5 Z",
                "S"),
            RelayRarityTier.Gold => new TierVisual(
                Color.FromRgb(103, 67, 5),
                Color.FromRgb(218, 158, 35),
                Color.FromRgb(255, 224, 120),
                "M12,0 L15,5 L20.5,3.5 L19,9 L24,12 L19,15 L20.5,20.5 L15,19 L12,24 L9,19 L3.5,20.5 L5,15 L0,12 L5,9 L3.5,3.5 L9,5 Z",
                "G"),
            RelayRarityTier.Platinum => new TierVisual(
                Color.FromRgb(35, 77, 92),
                Color.FromRgb(92, 202, 220),
                Color.FromRgb(222, 254, 255),
                "M12,0 L24,8 L12,24 L0,8 Z",
                "P"),
            _ => new TierVisual(
                Color.FromRgb(64, 68, 71),
                Color.FromRgb(143, 149, 152),
                Color.FromRgb(225, 229, 230),
                "M6,1 L18,1 L24,12 L18,23 L6,23 L0,12 Z",
                "?")
        };
    }

    private sealed class AchievementOverlayAutomationPeer(AchievementOverlayWindow owner)
        : WindowAutomationPeer(owner)
    {
        protected override List<AutomationPeer> GetChildrenCore() => null!;
    }
}
