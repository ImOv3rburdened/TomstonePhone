using System.Globalization;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace TomestonePhone.UI;

public sealed class PhoneTheme : IDisposable
{
    private readonly List<IDisposable> scopes = [];

    public static PhoneTheme Push(Configuration configuration)
    {
        var accent = ParseColor(configuration.AccentColorHex, new Vector4(0.85f, 0.71f, 0.43f, 1f));
        var surface = new Vector4(0.04f, 0.05f, 0.07f, 0.7f);
        var panel = new Vector4(0.14f, 0.17f, 0.22f, 0.58f);
        var panelAlt = new Vector4(0.19f, 0.23f, 0.29f, 0.84f);
        var text = new Vector4(0.97f, 0.98f, 0.99f, 1f);

        var theme = new PhoneTheme();
        theme.PushStyle(ImGuiStyleVar.WindowRounding, 40f);
        theme.PushStyle(ImGuiStyleVar.FrameRounding, 20f);
        theme.PushStyle(ImGuiStyleVar.ChildRounding, 24f);
        theme.PushStyle(ImGuiStyleVar.PopupRounding, 24f);
        theme.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(20f, 20f));
        theme.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(12f, 10f));
        theme.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(12f, 12f));

        theme.PushColor(ImGuiCol.WindowBg, surface);
        theme.PushColor(ImGuiCol.ChildBg, panel);
        theme.PushColor(ImGuiCol.Border, new Vector4(1f, 1f, 1f, 0.08f));
        theme.PushColor(ImGuiCol.FrameBg, panel);
        theme.PushColor(ImGuiCol.FrameBgHovered, panelAlt);
        theme.PushColor(ImGuiCol.FrameBgActive, panelAlt);
        theme.PushColor(ImGuiCol.Button, new Vector4(0.2f, 0.25f, 0.34f, 0.72f));
        theme.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.31f, 0.42f, 0.88f));
        theme.PushColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.37f, 0.5f, 0.98f));
        theme.PushColor(ImGuiCol.Header, WithAlpha(accent, 0.18f));
        theme.PushColor(ImGuiCol.HeaderHovered, WithAlpha(accent, 0.24f));
        theme.PushColor(ImGuiCol.HeaderActive, WithAlpha(accent, 0.3f));
        theme.PushColor(ImGuiCol.Separator, new Vector4(1f, 1f, 1f, 0.1f));
        theme.PushColor(ImGuiCol.Tab, panelAlt);
        theme.PushColor(ImGuiCol.TabHovered, new Vector4(0.28f, 0.34f, 0.45f, 0.96f));
        theme.PushColor(ImGuiCol.TabActive, new Vector4(0.32f, 0.39f, 0.52f, 1f));
        theme.PushColor(ImGuiCol.Text, text);

        return theme;
    }

    public void Dispose()
    {
        for (var index = this.scopes.Count - 1; index >= 0; index--)
        {
            this.scopes[index].Dispose();
        }

        this.scopes.Clear();
    }

    private static Vector4 ParseColor(string value, Vector4 fallback)
    {
        if (value.Length == 7 && value[0] == '#')
        {
            var r = ParseByte(value[1..3]);
            var g = ParseByte(value[3..5]);
            var b = ParseByte(value[5..7]);
            return new Vector4(r / 255f, g / 255f, b / 255f, 1f);
        }

        return fallback;
    }

    private static byte ParseByte(string hex)
    {
        return byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : (byte)0;
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return new Vector4(color.X, color.Y, color.Z, alpha);
    }

    private void PushColor(ImGuiCol color, Vector4 value)
    {
        this.scopes.Add(ImRaii.PushColor(color, value));
    }

    private void PushStyle(ImGuiStyleVar style, float value)
    {
        this.scopes.Add(ImRaii.PushStyle(style, value));
    }

    private void PushStyle(ImGuiStyleVar style, Vector2 value)
    {
        this.scopes.Add(ImRaii.PushStyle(style, value));
    }
}

