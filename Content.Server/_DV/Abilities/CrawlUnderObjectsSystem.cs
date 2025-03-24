using Content.Shared.Actions;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared._DV.Abilities;
using Content.Shared.Maps;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Standing;
using Robust.Server.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Server._DV.Abilities;

public sealed partial class CrawlUnderObjectsSystem : SharedCrawlUnderObjectsSystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movespeed = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    // ShibaStation - Dictionary to track whether actions were removed due to standing state
    private readonly Dictionary<EntityUid, string> _removedActionsByStanding = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrawlUnderObjectsComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, ToggleCrawlingStateEvent>(OnAbilityToggle);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, AttemptClimbEvent>(OnAttemptClimb);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);

        // ShibaStation - Subscribe to the StoodEvent to disable sneaking when the entity stands up
        SubscribeLocalEvent<CrawlUnderObjectsComponent, StoodEvent>(OnStood);

        // ShibaStation - Subscribe to the DownedEvent to re-enable action when entity lies down
        SubscribeLocalEvent<CrawlUnderObjectsComponent, DownedEvent>(OnDowned);

        // ShibaStation - Handle cleanup when component is removed
        SubscribeLocalEvent<CrawlUnderObjectsComponent, ComponentShutdown>(OnShutdown);
    }

    // ShibaStation - Clean up any tracked actions when component is removed
    private void OnShutdown(EntityUid uid, CrawlUnderObjectsComponent component, ComponentShutdown args)
    {
        _removedActionsByStanding.Remove(uid);
    }

    // ShibaStation - Handler for when an entity lies down
    private void OnDowned(EntityUid uid, CrawlUnderObjectsComponent component, DownedEvent args)
    {
        // ShibaStation - If entity can sneak and the action isn't present, add it
        if (!component.SneakWhileStanding && component.ToggleHideAction == null && component.ActionProto != null)
        {
            _actionsSystem.AddAction(uid, ref component.ToggleHideAction, component.ActionProto);
            Dirty(uid, component);
        }
    }

    private void OnStood(EntityUid uid, CrawlUnderObjectsComponent component, StoodEvent args)
    {
        if (!component.SneakWhileStanding)
        {
            // ShibaStation - Disable sneaking when standing up
            if (component.Enabled)
            {
                DisableSneakMode(uid, component);

                if (TryComp<AppearanceComponent>(uid, out var app))
                    _appearance.SetData(uid, SneakMode.Enabled, false, app);

                _movespeed.RefreshMovementSpeedModifiers(uid);
            }

            // ShibaStation - Hide the action button when standing up
            if (component.ToggleHideAction.HasValue)
            {
                _actionsSystem.RemoveAction(uid, component.ToggleHideAction.Value);
                component.ToggleHideAction = null;
                Dirty(uid, component);
            }
        }
    }

    private bool IsOnCollidingTile(EntityUid uid)
    {
        var xform = Transform(uid);
        var tile = xform.Coordinates.GetTileRef();
        if (tile == null)
            return false;

        return _turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask);
    }

    private void OnInit(EntityUid uid, CrawlUnderObjectsComponent component, ComponentInit args)
    {
        // ShibaStation - Only add the action if the entity can sneak in their current state
        if (component.ToggleHideAction != null)
        {
            // ShibaStation - If entity can't sneak while standing and is currently standing, remove the action
            if (!component.SneakWhileStanding && !_standing.IsDown(uid))
            {
                _actionsSystem.RemoveAction(uid, component.ToggleHideAction.Value);
                component.ToggleHideAction = null;
                Dirty(uid, component);
            }
            return;
        }

        // ShibaStation - Only add the action if the entity can sneak in their current state
        if ((component.SneakWhileStanding || _standing.IsDown(uid)) && component.ActionProto != null)
        {
            _actionsSystem.AddAction(uid, ref component.ToggleHideAction, component.ActionProto);
        }
    }

    private bool EnableSneakMode(EntityUid uid, CrawlUnderObjectsComponent component)
    {
        if (component.Enabled
            || (TryComp<ClimbingComponent>(uid, out var climbing)
                && climbing.IsClimbing == true))
            return false;

        if (!component.SneakWhileStanding && !_standing.IsDown(uid))
            return false;

        component.Enabled = true;
        Dirty(uid, component);
        RaiseLocalEvent(uid, new CrawlingUpdatedEvent(component.Enabled));

        if (TryComp(uid, out FixturesComponent? fixtureComponent))
        {
            foreach (var (key, fixture) in fixtureComponent.Fixtures)
            {
                var newMask = (fixture.CollisionMask
                    & (int)~CollisionGroup.HighImpassable
                    & (int)~CollisionGroup.MidImpassable)
                    | (int)CollisionGroup.InteractImpassable;
                if (fixture.CollisionMask == newMask)
                    continue;

                component.ChangedFixtures.Add((key, fixture.CollisionMask));
                _physics.SetCollisionMask(uid,
                    key,
                    fixture,
                    newMask,
                    manager: fixtureComponent);
            }
        }
        return true;
    }

    private bool DisableSneakMode(EntityUid uid, CrawlUnderObjectsComponent component)
    {
        if (!component.Enabled || IsOnCollidingTile(uid) || (TryComp<ClimbingComponent>(uid, out var climbing) && climbing.IsClimbing == true)) {
            return false;
        }

        component.Enabled = false;
        Dirty(uid, component);
        RaiseLocalEvent(uid, new CrawlingUpdatedEvent(component.Enabled));

        // Restore normal collision masks
        if (TryComp(uid, out FixturesComponent? fixtureComponent))
            foreach (var (key, originalMask) in component.ChangedFixtures)
                if (fixtureComponent.Fixtures.TryGetValue(key, out var fixture))
                    _physics.SetCollisionMask(uid, key, fixture, originalMask, fixtureComponent);

        component.ChangedFixtures.Clear();
        return true;
    }

    private void OnAbilityToggle(EntityUid uid,
        CrawlUnderObjectsComponent component,
        ToggleCrawlingStateEvent args)
    {
        if (args.Handled)
            return;

        bool result;

        if (component.Enabled)
            result = DisableSneakMode(uid, component);
        else
        {
            if (!component.SneakWhileStanding && !_standing.IsDown(uid))
            {
                args.Handled = false;
                return;
            }

            result = EnableSneakMode(uid, component);
        }

        if (TryComp<AppearanceComponent>(uid, out var app))
            _appearance.SetData(uid, SneakMode.Enabled, component.Enabled, app);

        _movespeed.RefreshMovementSpeedModifiers(uid);

        args.Handled = result;
    }

    private void OnAttemptClimb(EntityUid uid,
        CrawlUnderObjectsComponent component,
        AttemptClimbEvent args)
    {
        if (component.Enabled == true)
            args.Cancelled = true;
    }

    private void OnRefreshMovespeed(EntityUid uid, CrawlUnderObjectsComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (component.Enabled)
            args.ModifySpeed(component.SneakSpeedModifier, component.SneakSpeedModifier);
    }
}
