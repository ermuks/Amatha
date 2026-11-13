using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Amaranth10API.Views;

namespace Amaranth10API.Helpers;

/// <summary>
/// 메시지 상자 대신 쓰는 토스트 알림.
/// 창 오른쪽 아래에 뜨고, 여러 개면 위로 쌓입니다. 메인 창이 소유한 별도 창이라
/// 메일 본문(WebView2)을 포함한 창 안 모든 화면보다 위에 보이며 포커스를 빼앗지 않습니다.
/// 메인 창이 보이지 않으면(트레이) 화면 오른쪽 아래에 항상 위로 띄웁니다.
/// </summary>
public static class Toast
{
    private const double EdgeGap = 12;   // 창 테두리와의 간격 (카드 그림자 여백 별도)
    private const double StackGap = -10; // 쌓을 때 겹치는 그림자 여백만큼 당김

    private static readonly List<ToastWindow> OpenToasts = new();
    private static readonly HashSet<Window> WatchedOwners = new();

    /// <summary>지금 떠 있는 알림 (테스트·확인용)</summary>
    public static IReadOnlyList<ToastWindow> Open => OpenToasts;

    public static ToastWindow? Info(Window? owner, string message, string? title = null) => Show(owner, message, title, ToastKind.Info);
    public static ToastWindow? Success(Window? owner, string message, string? title = null) => Show(owner, message, title, ToastKind.Success);
    public static ToastWindow? Warning(Window? owner, string message, string? title = null) => Show(owner, message, title, ToastKind.Warning);
    public static ToastWindow? Error(Window? owner, string message, string? title = null) => Show(owner, message, title, ToastKind.Error);

    public static ToastWindow? Show(Window? owner, string message, string? title = null, ToastKind kind = ToastKind.Info)
    {
        Application? app = Application.Current;
        if (app == null) return null;
        if (!app.Dispatcher.CheckAccess())
        {
            return app.Dispatcher.Invoke(() => Show(owner, message, title, kind));
        }

        owner ??= app.MainWindow;
        if (owner is ToastWindow) owner = owner.Owner;
        bool attach = owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized;

        var toast = new ToastWindow(title, message, kind);
        if (attach)
        {
            toast.Owner = owner;   // 소유 창은 항상 메인 창 위에 그려짐
            Watch(owner!);
        }
        else
        {
            toast.Topmost = true;  // 메인 창이 숨어 있으면 화면 맨 위에
        }

        toast.Closed += (_, _) =>
        {
            OpenToasts.Remove(toast);
            Relayout();
        };
        OpenToasts.Add(toast);
        toast.Left = -32000; toast.Top = -32000; // 위치를 잡기 전 깜빡임 방지 (처음엔 투명)
        toast.Show();
        Relayout();
        toast.StartTimeline();
        return toast;
    }

    /// <summary>떠 있는 알림을 모두 사라지게 합니다 (로그아웃·종료 시).</summary>
    public static void DismissAll()
    {
        foreach (ToastWindow toast in OpenToasts.ToArray()) toast.Dismiss();
    }

    private static void Watch(Window owner)
    {
        if (!WatchedOwners.Add(owner)) return;
        owner.LocationChanged += OwnerChanged;
        owner.SizeChanged += OwnerChanged;
        owner.StateChanged += OwnerChanged;
        owner.Closed += (_, _) =>
        {
            WatchedOwners.Remove(owner);
            owner.LocationChanged -= OwnerChanged;
            owner.SizeChanged -= OwnerChanged;
            owner.StateChanged -= OwnerChanged;
        };
    }

    private static void OwnerChanged(object? sender, EventArgs e) => Relayout();

    /// <summary>창(또는 화면)별로 오른쪽 아래에서 위로 쌓습니다. 새 알림이 맨 아래.</summary>
    private static void Relayout()
    {
        foreach (IGrouping<Window?, ToastWindow> group in OpenToasts.GroupBy(toast => toast.Owner))
        {
            Rect area = group.Key != null ? ClientArea(group.Key) : SystemParameters.WorkArea;
            double bottom = area.Bottom - EdgeGap;
            foreach (ToastWindow toast in group.Reverse())
            {
                double height = toast.ActualHeight > 0 ? toast.ActualHeight : 96;
                toast.Left = area.Right - EdgeGap - toast.Width;
                toast.Top = bottom - height;
                bottom = toast.Top - StackGap;
            }
        }
    }

    /// <summary>창의 화면 위치(DIP). 최대화하면 Left/Top이 복원 위치를 돌려주므로 모니터 작업 영역을 씁니다.</summary>
    private static Rect ClientArea(Window owner)
    {
        if (owner.WindowState == WindowState.Maximized)
        {
            IntPtr handle = new WindowInteropHelper(owner).Handle;
            if (handle != IntPtr.Zero)
            {
                System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
                DpiScale dpi = VisualTreeHelper.GetDpi(owner);
                return new Rect(work.Left / dpi.DpiScaleX, work.Top / dpi.DpiScaleY, work.Width / dpi.DpiScaleX, work.Height / dpi.DpiScaleY);
            }
        }

        return new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
    }
}
