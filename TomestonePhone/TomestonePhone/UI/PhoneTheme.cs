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
        var accent = ParseColor(configuration.AccentColorHex, new Vector4(0.87f, 0.73f, 0.46f, 1f));
        var window = new Vector4(0.035f, 0.045f, 0.07f, 0.84f);
        var panel = new Vector4(0.115f, 0.135f, 0.19f, 0.76f);
        var panelAlt = new Vector4(0.155f, 0.18f, 0.24f, 0.92f);
        var frame = new Vector4(0.13f, 0.16f, 0.22f, 0.88f);
        var frameHover = new Vector4(0.18f, 0.22f, 0.3f, 0.96f);
        var frameActive = new Vector4(0.22f, 0.27f, 0.36f, 1f);
        var text = new Vector4(0.96f, 0.975f, 0.995f, 1f);
        var muted = new Vector4(0.67f, 0.72f, 0.8f, 1f);

        var theme = new PhoneTheme();
        theme.PushStyle(ImGuiStyleVar.WindowRounding, 38f);
        theme.PushStyle(ImGuiStyleVar.ChildRounding, 26f);
        theme.PushStyle(ImGuiStyleVar.FrameRounding, 18f);
        theme.PushStyle(ImGuiStyleVar.PopupRounding, 24f);
        theme.PushStyle(ImGuiStyleVar.GrabRounding, 18f);
        theme.PushStyle(ImGuiStyleVar.ScrollbarRounding, 18f);
        theme.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(22f, 18f));
        theme.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(14f, 10f));
        theme.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(12f, 12f));
        theme.PushStyle(ImGuiStyleVar.ScrollbarSize, 8f);
        theme.PushStyle(ImGuiStyleVar.ChildBorderSize, 0f);
        theme.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f);

        theme.PushColor(ImGuiCol.WindowBg, window);
        theme.PushColor(ImGuiCol.ChildBg, panel);
        theme.PushColor(ImGuiCol.Border, new Vector4(1f, 1f, 1f, 0.08f));
        theme.PushColor(ImGuiCol.FrameBg, frame);
        theme.PushColor(ImGuiCol.FrameBgHovered, frameHover);
        theme.PushColor(ImGuiCol.FrameBgActive, frameActive);
        theme.PushColor(ImGuiCol.Button, new Vector4(0.18f, 0.22f, 0.31f, 0.9f));
        theme.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.24f, 0.3f, 0.41f, 0.98f));
        theme.PushColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.37f, 0.49f, 1f));
        theme.PushColor(ImGuiCol.Header, WithAlpha(accent, 0.18f));
        theme.PushColor(ImGuiCol.HeaderHovered, WithAlpha(accent, 0.24f));
        theme.PushColor(ImGuiCol.HeaderActive, WithAlpha(accent, 0.3f));
        theme.PushColor(ImGuiCol.Separator, new Vector4(1f, 1f, 1f, 0.09f));
        theme.PushColor(ImGuiCol.ScrollbarBg, new Vector4(0f, 0f, 0f, 0.12f));
        theme.PushColor(ImGuiCol.ScrollbarGrab, new Vector4(0.72f, 0.77f, 0.86f, 0.22f));
        theme.PushColor(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.78f, 0.82f, 0.9f, 0.34f));
        theme.PushColor(ImGuiCol.ScrollbarGrabActive, new Vector4(0.84f, 0.87f, 0.94f, 0.46f));
        theme.PushColor(ImGuiCol.CheckMark, accent);
        theme.PushColor(ImGuiCol.SliderGrab, accent);
        theme.PushColor(ImGuiCol.SliderGrabActive, new Vector4(accent.X, accent.Y, accent.Z, 1f));
        theme.PushColor(ImGuiCol.Tab, panelAlt);
        theme.PushColor(ImGuiCol.TabHovered, frameHover);
        theme.PushColor(ImGuiCol.TabActive, frameActive);
        theme.PushColor(ImGuiCol.Text, text);
        theme.PushColor(ImGuiCol.TextDisabled, muted);

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
