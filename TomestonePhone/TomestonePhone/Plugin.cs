using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using TomestonePhone.Networking;
using TomestonePhone.UI;

namespace TomestonePhone;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/tomestone";
    public const string CommandAlias = "/ts";

    private readonly Service service;
    private readonly Configuration configuration;
    private readonly WindowSystem windows = new("TomestonePhone");
    private readonly TomestonePhoneClient client;
    private readonly PhoneState state;
    private readonly PhoneWindow phoneWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IChatGui chatGui,
        ICommandManager commands,
        IPluginLog log,
        ITextureProvider textureProvider,
        IClientState clientState,
        IObjectTable objectTable,
        IFramework framework)
    {
        this.configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        this.service = new Service
        {
            PluginInterface = pluginInterface,
            ChatGui = chatGui,
            Commands = commands,
            Log = log,
            TextureProvider = textureProvider,
            ClientState = clientState,
            ObjectTable = objectTable,
            Framework = framework,
            Windows = this.windows,
        };

        this.state = PhoneState.CreateSeeded();
        this.client = new TomestonePhoneClient(this.configuration, log);
        this.phoneWindow = new PhoneWindow(this.service, this.configuration, this.state, this.client);

        this.windows.AddWindow(this.phoneWindow);

        this.service.PluginInterface.UiBuilder.Draw += this.DrawUi;
        this.service.PluginInterface.UiBuilder.OpenMainUi += this.ToggleUi;
        this.service.PluginInterface.UiBuilder.OpenConfigUi += this.OpenSettings;

        this.service.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the TomestonePhone UI.",
        });

        this.service.Commands.AddHandler(CommandAlias, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the TomestonePhone UI.",
        });

        if (!this.configuration.StartHidden)
        {
            this.phoneWindow.IsOpen = true;
        }
    }

    public string Name => "TomestonePhone";

    public void Dispose()
    {
        this.service.Commands.RemoveHandler(CommandName);
        this.service.Commands.RemoveHandler(CommandAlias);
        this.service.PluginInterface.UiBuilder.Draw -= this.DrawUi;
        this.service.PluginInterface.UiBuilder.OpenMainUi -= this.ToggleUi;
        this.service.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettings;
        this.windows.RemoveAllWindows();
        this.client.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Contains("config", StringComparison.OrdinalIgnoreCase))
        {
            this.phoneWindow.OpenSettingsTab();
            this.phoneWindow.IsOpen = true;
            return;
        }

        this.ToggleUi();
    }

    private void ToggleUi()
    {
        this.phoneWindow.IsOpen = !this.phoneWindow.IsOpen;
    }

    private void OpenSettings()
    {
        this.phoneWindow.OpenSettingsTab();
        this.phoneWindow.IsOpen = true;
    }

    private void DrawUi()
    {
        this.windows.Draw();
    }
}




