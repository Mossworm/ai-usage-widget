using System.Globalization;

namespace AiUsage;

public static class UiText
{
    public static bool IsKorean => string.Equals(
        CultureInfo.CurrentUICulture.Name, "ko-KR", StringComparison.OrdinalIgnoreCase);

    public static string Choose(string english, string korean) => IsKorean ? korean : english;
}
