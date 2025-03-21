using System.Linq;
using Content.Server.Anomaly;
using Content.Server.Spawners.Components;
using Content.Server.Station.Components;
using Content.Server.StationEvents.Components;
using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;

﻿using Content.Shared.GameTicking.Components;

namespace Content.Server.StationEvents.Events;

public sealed class AnomalySpawnRule : StationEventSystem<AnomalySpawnRuleComponent>
{
    [Dependency] private readonly AnomalySystem _anomaly = default!;
    [Dependency] private readonly EntityTableSystem _entityTable = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    protected override void Added(EntityUid uid, AnomalySpawnRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        if (!TryComp<StationEventComponent>(uid, out var stationEvent))
            return;

        var str = Loc.GetString("anomaly-spawn-event-announcement",
            ("sighting", Loc.GetString($"anomaly-spawn-sighting-{RobustRandom.Next(1, 6)}")));
        stationEvent.StartAnnouncement = str;

        base.Added(uid, component, gameRule, args);
    }

    protected override void Started(EntityUid uid, AnomalySpawnRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var chosenStation))
            return;

        if (!TryComp<StationDataComponent>(chosenStation, out var stationData))
            return;

        var grid = StationSystem.GetLargestGrid(stationData);

        if (grid is null)
            return;

        var amountToSpawn = 1;
        for (var i = 0; i < amountToSpawn; i++)
        {

            // SihbaStation – Dynamically use the RandomAnomalySpawner prototype to retrieve a list of anomalies to spawn and their weights. This should make things easier if changes are made in the future.
            var chosenAnomaly = GetAnomalySpawn(component.AnomalySpawnerPrototype);

            if (chosenAnomaly == "RandomAnomalyInjectorSpawner")
            {
                chosenAnomaly = GetAnomalySpawn("RandomAnomalyInjectorSpawner");
            }

            if (chosenAnomaly == "RandomRockAnomalySpawner")
            {
                chosenAnomaly = GetAnomalySpawn("RandomRockAnomalySpawner");
            }

            if (!string.IsNullOrEmpty(chosenAnomaly))
            {
                _anomaly.SpawnOnRandomGridLocation(grid.Value, chosenAnomaly, true);
            }

        }

    }

    private string? GetAnomalySpawn(string prototypeId)
    {
        var proto = _prototypeManager.Index<EntityPrototype>(prototypeId);
        if (!proto.TryGetComponent<EntityTableSpawnerComponent>(out var anomalies, EntityManager.ComponentFactory))
            return null;

        var spawns = _entityTable.GetSpawns(anomalies.Table);
        return spawns.FirstOrDefault();
    }
}
