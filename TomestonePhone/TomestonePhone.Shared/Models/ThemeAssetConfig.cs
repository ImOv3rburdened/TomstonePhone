namespace TomestonePhone.Shared.Models;

public sealed record ThemeAssetConfig(
    string BackgroundImagePath,
    string ContactsIconPath,
    string MessagesIconPath,
    string CallsIconPath,
    string FriendsIconPath,
    string AccentColorHex);
