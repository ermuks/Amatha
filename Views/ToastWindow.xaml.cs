using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Amaranth10API.Views;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// 화면 구석에 잠깐 떴다 사라지는 알림. 띄우기·쌓기·위치는 <see cref="Helpers.Toast"/>가 맡습니다.
/// 남은 시간 막대가 다 줄면 사라지고, 마우스를 올려 두면 멈춥니다. 오른쪽 x를 누르면 바로 사라지기 시작합니다.
/// </summary>
public partial class ToastWindow : Window
{
    /// <summary>처음 보여 주는 시간</summary>
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(6);

    /// <summary>글이 길어도 이보다 오래 보여 주지 않음</summary>
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(20);

    private const int FreeCharacters = 40;       // 이만큼은 기본 6초 안에 읽는다고 봄
    private const double CharactersPerSecond = 12; // 그 뒤로는 12자마다 1초씩 연장

    private static readonly Duration AppearDuration = new(TimeSpan.FromMilliseconds(220));
    private static readonly Duration DismissDuration = new(TimeSpan.FromMilliseconds(180));

    private AnimationClock? _countdown;
    private bool _dismissing;

    public ToastWindow(string? title, string message, ToastKind kind)
    {
        InitializeComponent();
        Kind = kind;
        TitleText.Text = title ?? string.Empty;
        TitleText.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = message ?? string.Empty;
        DisplayDuration = DurationFor(title, message);
        ApplyKind(kind);
        SourceInitialized += (_, _) => MakeNonActivating();
        MouseEnter += (_, _) => _countdown?.Controller?.Pause();
        MouseLeave += (_, _) => { if (!_dismissing) _countdown?.Controller?.Resume(); };
    }

    public ToastKind Kind { get; }

    /// <summary>이 알림을 보여 줄 시간 (마우스를 올린 동안은 멈춤)</summary>
    public TimeSpan DisplayDuration { get; }

    /// <summary>x 버튼이나 시간 만료로 사라지는 중인지</summary>
    public bool IsDismissing => _dismissing;

    /// <summary>
    /// 글자 수(공백 제외)에 따라 보여 줄 시간을 정합니다.
    /// 40자까지는 6초, 그 뒤로 12자마다 1초씩 늘고 최대 20초입니다.
    /// </summary>
    public static TimeSpan DurationFor(string? title, string? message)
    {
        int characters = 0;
        foreach (char c in (title ?? string.Empty) + (message ?? string.Empty))
        {
            if (!char.IsWhiteSpace(c)) characters++;
        }

        double seconds = MinimumDuration.TotalSeconds + Math.Max(0, characters - FreeCharacters) / CharactersPerSecond;
        return TimeSpan.FromSeconds(Math.Min(MaximumDuration.TotalSeconds, seconds));
    }

    /// <summary>나타나는 동작과 남은 시간 막대를 시작합니다. 창을 보이고 위치를 잡은 뒤 부릅니다.</summary>
    internal void StartTimeline()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, AppearDuration) { EasingFunction = ease });
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, AppearDuration) { EasingFunction = ease });

        var countdown = new DoubleAnimation(1, 0, new Duration(DisplayDuration));
        _countdown = countdown.CreateClock();
        _countdown.Completed += (_, _) => Dismiss();
        ProgressScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _countdown);
        if (IsMouseOver) _countdown.Controller?.Pause();
    }

    /// <summary>사라지는 동작(흐려지며 살짝 올라감)을 시작하고, 끝나면 창을 닫습니다.</summary>
    public void Dismiss()
    {
        if (_dismissing) return;
        _dismissing = true;
        _countdown?.Controller?.Pause();
        CloseButton.IsEnabled = false;
        IsHitTestVisible = false;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(Opacity, 0, DismissDuration) { EasingFunction = ease };
        fade.Completed += (_, _) => Close();
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Slide.Y, -8, DismissDuration) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, fade);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Dismiss();
    }

    private void ApplyKind(ToastKind kind)
    {
        (string color, string icon) = kind switch
        {
            ToastKind.Success => ("#2E9E5B", "M6,12.5 L10,16.5 L18,8"),
            ToastKind.Warning => ("#D97706", "M12,6 L12,13 M12,17.5 L12,18"),
            ToastKind.Error => ("#D9372B", "M12,6 L12,13 M12,17.5 L12,18"),
            _ => ("#5847C9", "M12,11 L12,17 M12,6.5 L12,7")
        };
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        IconBadge.Background = brush;
        ProgressBar.Fill = brush;
        IconPath.Data = Geometry.Parse(icon);
    }

    // 눌러도 메인 창의 키보드 포커스를 빼앗지 않고, Alt+Tab 목록에도 나오지 않게 합니다.
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private void MakeNonActivating()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
