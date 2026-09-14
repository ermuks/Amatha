using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Amaranth10API.Helpers;

internal static class ScreenPlacement
{
    public static Point CursorDip()
    {
        return ToDip(Forms.Cursor.Position);
    }

    public static Rect WorkingAreaDip()
    {
        System.Drawing.Rectangle area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        Point topLeft = ToDip(new System.Drawing.Point(area.Left, area.Top));
        Point bottomRight = ToDip(new System.Drawing.Point(area.Right, area.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public static void StealFocus(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetForegroundWindow(hwnd);
        window.Activate();
        window.Focus();
    }

    private static Point ToDip(System.Drawing.Point screen)
    {
        using Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
        double scaleX = 96.0 / graphics.DpiX;
        double scaleY = 96.0 / graphics.DpiY;
        return new Point(screen.X * scaleX, screen.Y * scaleY);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
