namespace SteamShare.UI.ViewModels;

/// <summary>
/// A selectable option that holds both a raw value (used programmatically)
/// and a localized display string (shown to the user).
/// </summary>
public sealed record LocalizedOption(string Value, string Display)
{
    public override string ToString() => Display;
}
