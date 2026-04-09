using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace TomestonePhone;

public sealed class Service
{
    public required IDalamudPluginInterface PluginInterface { get; init; }

    public required IChatGui ChatGui { get; init; }

    public required ICommandManager Commands { get; init; }

    public required IPluginLog Log { get; init; }

    public required ITextureProvider TextureProvider { get; init; }

    public required IClientState ClientState { get; init; }

    public required IPlayerState PlayerState { get; init; }

    public required IObjectTable ObjectTable { get; init; }

    public required IFramework Framework { get; init; }

    public required WindowSystem Windows { get; init; }
}



