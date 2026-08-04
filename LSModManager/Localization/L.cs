using System.Globalization;

namespace LSModManager.Localization;

/// <summary>
/// Kurz-Helper für ViewModels: <c>L.T("Status_Ready")</c> statt
/// <c>LocalizationService.Instance["Status_Ready"]</c>, plus
/// <c>L.F("Downloaded", count, total)</c> für parametrisierte Strings
/// mit <see cref="string.Format(string, object[])"/>.
/// </summary>
internal static class L
{
    public static string T(string key) => LocalizationService.Instance[key];

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, LocalizationService.Instance[key], args);
}
