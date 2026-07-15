using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using TomestonePhone.Networking;
using TomestonePhone.UI;

namespace TomestonePhone;

public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/ts";

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
        IPlayerState playerState,
        IObjectTable objectTable,
        IFramework framework)
    {
        this.configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.NormalizeServerBaseUrl();
        this.configuration.NormalizeAssetPaths();
        pluginInterface.SavePluginConfig(this.configuration);

        this.service = new Service
        {
            PluginInterface = pluginInterface,
            ChatGui = chatGui,
            Commands = commands,
            Log = log,
            TextureProvider = textureProvider,
            ClientState = clientState,
            PlayerState = playerState,
            ObjectTable = objectTable,
            Framework = framework,
            Windows = this.windows,
        };

        this.state = PhoneState.CreateSeeded();
        this.client = new TomestonePhoneClient(this.configuration, log);
        this.phoneWindow = new PhoneWindow(this.service, this.configuration, this.state, this.client);

        this.windows.AddWindow(this.phoneWindow);
        this.phoneWindow.RegisterOverlayWindows(this.windows);

        this.service.PluginInterface.UiBuilder.DisableGposeUiHide = true;

        this.service.PluginInterface.UiBuilder.Draw += this.DrawUi;
        this.service.PluginInterface.UiBuilder.OpenMainUi += this.ToggleUiWithoutCommandEmote;
        this.service.PluginInterface.UiBuilder.OpenConfigUi += this.OpenSettings;

        this.service.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
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
        this.service.PluginInterface.UiBuilder.Draw -= this.DrawUi;
        this.service.PluginInterface.UiBuilder.OpenMainUi -= this.ToggleUiWithoutCommandEmote;
        this.service.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettings;
        this.phoneWindow.DisposeResources();
        this.windows.RemoveAllWindows();
        this.client.Dispose();
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Contains("config", StringComparison.OrdinalIgnoreCase))
        {
            this.phoneWindow.OpenSettingsTab();
            this.SetPhoneOpenState(true, true);
            return;
        }

        this.ToggleUi();
    }

    private void ToggleUi()
    {
        this.SetPhoneOpenState(!this.phoneWindow.IsOpen, true);
    }

    private void ToggleUiWithoutCommandEmote()
    {
        this.SetPhoneOpenState(!this.phoneWindow.IsOpen, false);
    }

    private void OpenSettings()
    {
        this.phoneWindow.OpenSettingsTab();
        this.SetPhoneOpenState(true, false);
    }

    private void SetPhoneOpenState(bool isOpen, bool triggerCommandEmote)
    {
        var wasOpen = this.phoneWindow.IsOpen;
        this.phoneWindow.IsOpen = isOpen;

        if (!wasOpen && isOpen && triggerCommandEmote && this.configuration.PlayOpenEmote)
        {
            this.TryPlayOpenEmote();
        }
    }

    private void TryPlayOpenEmote()
    {
        try
        {
            if (this.service.Framework.IsInFrameworkUpdateThread)
            {
                this.ExecuteOpenEmoteCommand();
                return;
            }

            _ = this.service.Framework.Run(this.ExecuteOpenEmoteCommand);
        }
        catch (Exception ex)
        {
            this.service.Log.Warning(ex, "Failed to play /tomestone when opening the phone.");
        }
    }

    private unsafe void ExecuteOpenEmoteCommand()
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            this.service.Log.Warning("Failed to play /tomestone when opening the phone because UIModule.Instance() was null.");
            return;
        }

        using var command = new Utf8String("/tomestone");
        uiModule->ProcessChatBoxEntry(&command, saveToHistory: false);
    }

    private void DrawUi()
    {
        this.phoneWindow.TickBackground();
        this.windows.Draw();
    }
}





