using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.UserInterface;
using Content.Shared._OE14.CommunicationCrystal;
using Content.Shared._OE14.CommunicationCrystal.Components;
using Content.Shared._OE14.MagicEnergy.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Content.Shared.Popups;
using Content.Shared.Interaction;
using Content.Shared.Hands.EntitySystems;
using Content.Server._OE14.MagicEnergy;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;

namespace Content.Server._OE14.CommunicationCrystal;

public sealed partial class OE14CommunicationCrystalSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly OE14MagicEnergySystem _magicEnergy = default!;
    [Dependency] private readonly Robust.Shared.Timing.IGameTiming _timing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OE14CommunicationCrystalComponent, BeforeActivatableUIOpenEvent>(OnBeforeUIOpen);
        SubscribeLocalEvent<OE14CommunicationCrystalComponent, OE14CommunicationCrystalSendMessage>(OnSendMessage);
        SubscribeLocalEvent<OE14CommunicationCrystalComponent, OE14CommunicationCrystalRemoveCrystal>(OnRemoveCrystal);
        SubscribeLocalEvent<OE14CommunicationCrystalComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnRemoveCrystal(Entity<OE14CommunicationCrystalComponent> ent, ref OE14CommunicationCrystalRemoveCrystal args)
    {
        if (!TryComp<ContainerManagerComponent>(ent, out var containerManager))
            return;

        if (!_container.TryGetContainer(ent, "OE14CommunicationCrystalStorage", out var container, containerManager))
            return;

        if (container.ContainedEntities.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-no-crystal"), ent);
            UpdateUIState(ent);
            return;
        }

        var crystal = container.ContainedEntities[0];
        _container.Remove(crystal, container);

        _audio.PlayPvs("/Audio/Magic/ethereal_enter.ogg", Transform(ent).Coordinates);
        _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-removed"), ent);

        UpdateUIState(ent);
    }

    private void OnInteractUsing(Entity<OE14CommunicationCrystalComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ContainerManagerComponent>(ent, out var containerManager))
            return;

        if (!_container.TryGetContainer(ent, "OE14CommunicationCrystalStorage", out var container, containerManager))
            return;

        if (container.ContainedEntities.Count > 0)
        {
            _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-already-has"), ent, args.User);
            return;
        }

        if (!HasComp<OE14MagicEnergyContainerComponent>(args.Used))
        {
            _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-need-energy"), ent, args.User);
            return;
        }

        _container.Insert(args.Used, container);
        _audio.PlayPvs("/Audio/Magic/ethereal_enter.ogg", Transform(ent).Coordinates);
        _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-inserted"), ent, args.User);

        UpdateUIState(ent);
        args.Handled = true;
    }

    private void OnBeforeUIOpen(Entity<OE14CommunicationCrystalComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUIState(ent);
    }

    private void OnSendMessage(Entity<OE14CommunicationCrystalComponent> ent, ref OE14CommunicationCrystalSendMessage args)
    {
        var cost = args.IsGlobal
            ? OE14CommunicationCrystalComponent.GlobalCost
            : OE14CommunicationCrystalComponent.LocalCost;

        if (string.IsNullOrWhiteSpace(args.Message))
            return;

        if (args.Message.Length > 500)
        {
            _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-message-too-long"), ent);
            return;
        }

        if (!TryComp<ContainerManagerComponent>(ent, out var containerManager))
            return;

        if (!_container.TryGetContainer(ent, "OE14CommunicationCrystalStorage", out var container, containerManager))
            return;

        var energyCrystal = container.ContainedEntities.Count > 0 ? container.ContainedEntities[0] : EntityUid.Invalid;
        if (energyCrystal == EntityUid.Invalid)
        {
            _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-no-crystal"), ent);
            UpdateUIState(ent);
            return;
        }

        if (!TryComp<OE14MagicEnergyContainerComponent>(energyCrystal, out var crystalEnergy))
        {
            _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-no-crystal"), ent);
            UpdateUIState(ent);
            return;
        }

        if (crystalEnergy.Energy < cost)
        {
            _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-insufficient-energy",
                ("current", (int)crystalEnergy.Energy),
                ("required", cost)), ent);
            UpdateUIState(ent);
            return;
        }

        if (args.IsGlobal)
        {
            var cooldown = ent.Comp.LastGlobalAnnouncement;
            if (cooldown != null)
            {
                var elapsed = _timing.CurTime - cooldown.Value;
                if (elapsed.TotalSeconds < OE14CommunicationCrystalComponent.GlobalCooldownSeconds)
                {
                    var remaining = OE14CommunicationCrystalComponent.GlobalCooldownSeconds - elapsed.TotalSeconds;
                    _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-global-cooldown",
                        ("seconds", (int)remaining)), ent);
                    return;
                }
            }

            ent.Comp.LastGlobalAnnouncement = _timing.CurTime;
            Dirty(ent);
        }

        _magicEnergy.ChangeEnergy(energyCrystal, -cost, out var delta, out var overload);

        var sender = Loc.GetString("oe14-comm-crystal-announcer");
        if (args.IsGlobal)
        {
            var message = args.Message.Trim();

            var formatted =
                $"\n" +
                $"──────────────────────────────\n\n" +
                $"{message}\n";

            _chat.DispatchGlobalAnnouncement(formatted, sender, playSound: true);
        }
        else
        {
        var message = args.Message.Trim().ToLower();

        // estilo chat simples
        var formatted = $"{sender}: {message}";

        // sem som
        _chat.DispatchStationAnnouncement(ent, formatted); // sem o parametro sender 
        }

        _audio.PlayPvs("/Audio/Magic/ethereal_enter.ogg", Transform(ent).Coordinates);

        _popup.PopupEntity(Loc.GetString("oe14-comm-crystal-sent"), ent);

        UpdateUIState(ent);
    }

    private void UpdateUIState(Entity<OE14CommunicationCrystalComponent> ent)
    {
        int currentEnergy = 0;
        int maxEnergy = 50;
        var hasCrystal = false;
        var canSendGlobal = false;

        if (TryComp<ContainerManagerComponent>(ent, out var containerManager))
        {
            if (_container.TryGetContainer(ent, "OE14CommunicationCrystalStorage", out var container, containerManager))
            {
                var crystal = container.ContainedEntities.Count > 0 ? container.ContainedEntities[0] : EntityUid.Invalid;
                if (crystal != EntityUid.Invalid && TryComp<OE14MagicEnergyContainerComponent>(crystal, out var crystalEnergy))
                {
                    hasCrystal = true;
                    currentEnergy = (int)crystalEnergy.Energy;
                    maxEnergy = (int)crystalEnergy.MaxEnergy;
                    canSendGlobal = currentEnergy >= OE14CommunicationCrystalComponent.GlobalCost;
                }
            }
        }

        var cooldown = ent.Comp.LastGlobalAnnouncement;
        TimeSpan? remainingCooldown = null;
        if (cooldown != null)
        {
            var elapsed = _timing.CurTime - cooldown.Value;
            if (elapsed.TotalSeconds < OE14CommunicationCrystalComponent.GlobalCooldownSeconds)
            {
                remainingCooldown = TimeSpan.FromSeconds(
                    OE14CommunicationCrystalComponent.GlobalCooldownSeconds - elapsed.TotalSeconds);
            }
        }

        var state = new OE14CommunicationCrystalUiState(
            currentEnergy,
            maxEnergy,
            hasCrystal,
            canSendGlobal,
            remainingCooldown);

        _userInterface.SetUiState(ent.Owner, OE14CommunicationCrystalUiKey.Board, state);
    }
}