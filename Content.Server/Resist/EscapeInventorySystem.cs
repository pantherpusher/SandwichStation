using Content.Server.Popups;
using Content.Shared.Storage;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Resist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server.Resist;

public sealed class EscapeInventorySystem : SharedEscapeInventorySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    /// <summary>
    /// DeltaV - action to cancel inventory escape
    /// </summary>
    [ValidatePrototypeId<EntityPrototype>]
    private readonly string _escapeCancelAction = "ActionCancelEscape";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CanEscapeInventoryComponent, EscapeInventoryCancelActionEvent>(OnCancelEscape);
    }

    public override void AttemptEscape(EntityUid user, EntityUid container, CanEscapeInventoryComponent component, float multiplier = 1f)
    {
        if (!_actionBlockerSystem.CanInteract(user, container))
            return;

        if (!_containerSystem.TryGetContainingContainer((uid, null, null), out var container) || !_actionBlockerSystem.CanInteract(uid, container.Owner))
            return;

        // Make sure there's nothing stopped the removal (like being glued)
        if (!_containerSystem.CanRemove(uid, container))
        {
            _popupSystem.PopupEntity(Loc.GetString("escape-inventory-component-failed-resisting"), uid, uid);
            return;
        }

        // Contested
        if (_handsSystem.IsHolding(container.Owner, uid, out _))
        {
            AttemptEscape(uid, container.Owner, component);
            return;
        }

        // Uncontested
        if (HasComp<StorageComponent>(container.Owner) || HasComp<InventoryComponent>(container.Owner) || HasComp<SecretStashComponent>(container.Owner))
            AttemptEscape(uid, container.Owner, component);
    }

    public void AttemptEscape(EntityUid user, EntityUid container, CanEscapeInventoryComponent component, float multiplier = 1f) //private to public for carrying system.
    {
        if (component.IsEscaping)
            return;

        var doAfterEventArgs = new DoAfterArgs(EntityManager, user, component.BaseResistTime * multiplier, new EscapeInventoryEvent(), user, target: container)
        {
            BreakOnDamage = true,
            NeedHand = false,
            CancelDuplicate = false, // Goobstation
        };

        if (!_doAfterSystem.TryStartDoAfter(doAfterEventArgs, out component.DoAfter))
            return;

        _popupSystem.PopupEntity(Loc.GetString("escape-inventory-component-start-resisting"), user, user);
        _popupSystem.PopupEntity(Loc.GetString("escape-inventory-component-start-resisting-target"), container, container);

        // DeltaV - escape cancel action
        if (component.EscapeCancelAction is not { Valid: true })
            _actions.AddAction(user, ref component.EscapeCancelAction, _escapeCancelAction);
    }

    // DeltaV
    private void OnCancelEscape(EntityUid uid, CanEscapeInventoryComponent component, EscapeInventoryCancelActionEvent args)
    {
        if (component.DoAfter != null)
            _doAfterSystem.Cancel(component.DoAfter);

        _actions.RemoveAction(uid, component.EscapeCancelAction);
        component.EscapeCancelAction = null;
    }
}
