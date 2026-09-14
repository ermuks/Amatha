using System.Windows.Media;

namespace Amaranth10API.Helpers;

public static class SchedulePalette
{
    public static readonly Color Navy = Color.FromRgb(62, 82, 112);
    public static readonly Color NavySoft = Color.FromRgb(122, 142, 168);
    public static readonly Color Sage = Color.FromRgb(122, 155, 138);
    public static readonly Color Periwinkle = Color.FromRgb(139, 145, 184);
    public static readonly Color Sand = Color.FromRgb(186, 164, 132);
    public static readonly Color Rose = Color.FromRgb(176, 132, 148);
    public static readonly Color Mist = Color.FromRgb(154, 168, 184);
    public static readonly Color Warning = Color.FromRgb(176, 122, 122);

    public static Color GetAccent(string name)
    {
        if (name.Contains("보고서 미작성"))
        {
            return Warning;
        }

        if (name.Contains("출장"))
        {
            return Navy;
        }

        if (name.Contains("연차") || name.Contains("반차"))
        {
            return Sage;
        }

        if (name.Contains("재택"))
        {
            return Periwinkle;
        }

        if (name.Contains("대체휴가") || name.Contains("대체휴무"))
        {
            return Sand;
        }

        if (name.Contains("휴일근무"))
        {
            return Rose;
        }

        return Mist;
    }

    public static SolidColorBrush GetBrush(string name)
    {
        return new SolidColorBrush(GetAccent(name));
    }

    public static SolidColorBrush GetSoftBrush(string name)
    {
        Color accent = GetAccent(name);
        return new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B));
    }
}
