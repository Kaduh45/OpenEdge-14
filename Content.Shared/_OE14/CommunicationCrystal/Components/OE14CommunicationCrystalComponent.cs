using Robust.Shared.GameStates;

namespace Content.Shared._OE14.CommunicationCrystal.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class OE14CommunicationCrystalComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan? LastGlobalAnnouncement { get; set; }

    public const int GlobalCost = 25;
    public const int LocalCost = 5;
    public const int GlobalCooldownSeconds = 120;
}