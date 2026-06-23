using System;
using System.Collections.Generic;

namespace Knapcode.FactorioTools.OilField;

/// <summary>
/// Routes a single connected heat pipe network adjacent to every pumpjack and every pipe so the oil field can survive
/// on Aquilo (Factorio 2.0 / Space Age), where unheated entities freeze. Heat conducts through the heat pipe network, so
/// every pumpjack and pipe only needs one adjacent heat pipe; the network is grown as one connected component so heat
/// from a single (user-placed) heat source reaches all of it.
///
/// This runs after the pumpjacks, pipes, underground pipes, and electric poles are on the grid and fills remaining empty
/// tiles. The covering + connectivity problem is NP-hard, so this is a greedy heuristic that grows one connected network:
/// it seeds with the highest-coverage tile, then repeatedly adds the network-adjacent tile that covers the most still
/// uncovered targets, bridging across gaps with a breadth-first search when no adjacent tile makes progress. Growing a
/// single connected component guarantees both coverage and connectivity without dropping any covering tiles.
/// </summary>
public static class AddHeatPipes
{
    // The 12 tiles orthogonally adjacent to a 3x3 pumpjack footprint. A heat pipe in any of these heats the pumpjack.
    internal static readonly Location[] PumpjackRingOffsets = new Location[]
    {
        new Location(-1, -2), new Location(0, -2), new Location(1, -2),
        new Location(-1, 2), new Location(0, 2), new Location(1, 2),
        new Location(-2, -1), new Location(-2, 0), new Location(-2, 1),
        new Location(2, -1), new Location(2, 0), new Location(2, 1),
    };

    public static void Execute(Context context)
    {
        // When beacons are also enabled, the heat network was already routed (before beacon planning) and
        // placed during AddPipes so beacons could route around it. Don't re-route over the placed entities.
        if (!context.Options.AddHeatPipes || context.HeatPipes is not null)
        {
            return;
        }

        var grid = context.Grid;

        // Collect the targets that must be heated: every pipe tile, and every pumpjack center.
        var pipeTiles = context.GetLocationSet(allowEnumerate: true);
        foreach (var location in grid.EntityLocations.EnumerateItems())
        {
            if (grid[location] is Pipe)
            {
                pipeTiles.Add(location);
            }
        }

        var chosen = RouteCore(context, pipeTiles, out _);

        // Place the heat pipe entities.
        foreach (var location in chosen.EnumerateItems())
        {
            grid.AddEntity(location, new HeatPipe(grid.GetId()));
        }

        context.HeatPipes = chosen;
    }

    /// <summary>
    /// Routes the heat pipe network for a given set of pipe tiles and returns the chosen heat tiles without placing any
    /// entities. The caller must have the pipe tiles occupying the grid (so they are not considered empty) - this lets
    /// beacon planning route heat first and place beacons around it. The grid is left unchanged.
    /// <paramref name="coversAllTargets"/> is false when this pipe layout cannot be fully heated (some pipe or pumpjack
    /// has no reachable empty tile), so the caller can prefer a heatable layout - heat is the hard constraint.
    /// </summary>
    public static ILocationSet Route(Context context, ILocationSet pipeTiles, out bool coversAllTargets)
    {
        var grid = context.Grid;

        // Occupy the pipe tiles so heat tiles are not chosen on top of them, then route and restore the grid.
        foreach (var pipe in pipeTiles.EnumerateItems())
        {
            grid.AddEntity(pipe, new TemporaryEntity(grid.GetId()));
        }

        var chosen = RouteCore(context, pipeTiles, out coversAllTargets);

        foreach (var pipe in pipeTiles.EnumerateItems())
        {
            grid.RemoveEntity(pipe);
        }

        return chosen;
    }

    /// <summary>
    /// The core greedy routing: builds candidate heat tiles from the targets and grows one connected network covering
    /// them. Assumes the pipe tiles (and pumpjacks) already occupy the grid so candidate tiles are genuinely empty.
    /// </summary>
    private static ILocationSet RouteCore(Context context, ILocationSet pipeTiles, out bool coversAllTargets)
    {
        var grid = context.Grid;

        // Build candidate heat pipe tiles (empty tiles that cover at least one target) and what each one covers.
        var candidates = new List<Location>();
        var coveredPipes = context.GetLocationDictionary<List<Location>>();
        var coveredCenters = context.GetLocationDictionary<List<Location>>();

#if USE_STACKALLOC && LOCATION_AS_STRUCT
        Span<Location> adjacent = stackalloc Location[4];
#else
        Span<Location> adjacent = new Location[4];
#endif

        foreach (var pipe in pipeTiles.EnumerateItems())
        {
            grid.GetAdjacent(adjacent, pipe);
            for (var i = 0; i < adjacent.Length; i++)
            {
                var candidate = adjacent[i];
                if (!candidate.IsValid || !grid.IsEmpty(candidate))
                {
                    continue;
                }

                AddCoverage(coveredPipes, candidates, candidate, pipe);
            }
        }

        for (var c = 0; c < context.Centers.Count; c++)
        {
            var center = context.Centers[c];
            for (var i = 0; i < PumpjackRingOffsets.Length; i++)
            {
                var candidate = center.Translate(PumpjackRingOffsets[i]);
                if (!grid.IsInBounds(candidate) || !grid.IsEmpty(candidate))
                {
                    continue;
                }

                AddCoverage(coveredCenters, candidates, candidate, center);
            }
        }

        var chosen = context.GetLocationSet(allowEnumerate: true);

        var uncoveredPipes = context.GetLocationSet(allowEnumerate: true);
        uncoveredPipes.UnionWith(pipeTiles);
        var uncoveredCenters = context.GetLocationSet(allowEnumerate: true);
        for (var c = 0; c < context.Centers.Count; c++)
        {
            uncoveredCenters.Add(context.Centers[c]);
        }

        if (candidates.Count > 0)
        {
            Grow(context, chosen, candidates, coveredPipes, coveredCenters, uncoveredPipes, uncoveredCenters);
        }

        coversAllTargets = uncoveredPipes.Count == 0 && uncoveredCenters.Count == 0;

        return chosen;
    }

    /// <summary>
    /// Grows a single connected heat pipe network. Seeds with the highest-coverage candidate, then repeatedly adds the
    /// network-adjacent candidate with the most uncovered coverage; when no adjacent candidate makes progress it bridges
    /// to the best remaining candidate via a shortest empty-tile path. Stops when everything reachable is covered.
    /// </summary>
    private static void Grow(
        Context context,
        ILocationSet chosen,
        List<Location> candidates,
        ILocationDictionary<List<Location>> coveredPipes,
        ILocationDictionary<List<Location>> coveredCenters,
        ILocationSet uncoveredPipes,
        ILocationSet uncoveredCenters)
    {
        var grid = context.Grid;

#if USE_STACKALLOC && LOCATION_AS_STRUCT
        Span<Location> adjacent = stackalloc Location[4];
#else
        Span<Location> adjacent = new Location[4];
#endif

        // Seed with the candidate that covers the most targets, but prefer one the network can actually grow from: a
        // candidate with at least one empty orthogonal neighbor ("expandable"). The highest-coverage tile can be fully
        // enclosed by pumpjacks and pipes (e.g. a tile wedged between two pumpjacks and two pipe runs). Bridging always
        // walks outward from the chosen network through empty tiles, so an enclosed seed can never expand and the rest
        // of the field is abandoned. Falling back to the plain best only when nothing is expandable.
        var seed = Location.Invalid;
        var seedGain = -1;
        var expandableSeed = Location.Invalid;
        var expandableSeedGain = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var gain = Gain(coveredPipes, coveredCenters, candidate, uncoveredPipes, uncoveredCenters);
            if (gain > seedGain)
            {
                seedGain = gain;
                seed = candidate;
            }

            if (gain > expandableSeedGain && HasEmptyNeighbor(grid, candidate, adjacent))
            {
                expandableSeedGain = gain;
                expandableSeed = candidate;
            }
        }

        if (expandableSeed.IsValid)
        {
            seed = expandableSeed;
        }

        AddTile(coveredPipes, coveredCenters, chosen, seed, uncoveredPipes, uncoveredCenters);

        // Candidates we could not reach by bridging; skip them on later passes to avoid looping.
        var unreachable = context.GetLocationSet();

        while (uncoveredPipes.Count > 0 || uncoveredCenters.Count > 0)
        {
            // Fast path: the best candidate orthogonally adjacent to the current network (keeps it connected for free).
            var bestAdjacent = Location.Invalid;
            var bestAdjacentGain = 0;

            // Bridge fallback: the best candidate anywhere that still covers something and we have not failed to reach.
            var bestAny = Location.Invalid;
            var bestAnyGain = 0;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (chosen.Contains(candidate))
                {
                    continue;
                }

                var gain = Gain(coveredPipes, coveredCenters, candidate, uncoveredPipes, uncoveredCenters);
                if (gain == 0)
                {
                    continue;
                }

                if (!unreachable.Contains(candidate) && gain > bestAnyGain)
                {
                    bestAnyGain = gain;
                    bestAny = candidate;
                }

                if (gain > bestAdjacentGain && IsAdjacentToNetwork(grid: context.Grid, chosen, candidate, adjacent))
                {
                    bestAdjacentGain = gain;
                    bestAdjacent = candidate;
                }
            }

            if (bestAdjacentGain > 0)
            {
                AddTile(coveredPipes, coveredCenters, chosen, bestAdjacent, uncoveredPipes, uncoveredCenters);
                continue;
            }

            if (bestAnyGain == 0)
            {
                // Remaining targets have no empty candidate tile at all - they are boxed in and cannot be heated.
                break;
            }

            // Bridge the network out to the best remaining candidate.
            var path = BridgeToTile(context, chosen, bestAny);
            if (path is null)
            {
                unreachable.Add(bestAny);
                continue;
            }

            for (var i = 0; i < path.Count; i++)
            {
                AddTile(coveredPipes, coveredCenters, chosen, path[i], uncoveredPipes, uncoveredCenters);
            }
        }
    }

    private static int Gain(
        ILocationDictionary<List<Location>> coveredPipes,
        ILocationDictionary<List<Location>> coveredCenters,
        Location candidate,
        ILocationSet uncoveredPipes,
        ILocationSet uncoveredCenters)
    {
        return CountCovered(coveredPipes, candidate, uncoveredPipes)
            + CountCovered(coveredCenters, candidate, uncoveredCenters);
    }

    private static bool HasEmptyNeighbor(SquareGrid grid, Location location, Span<Location> adjacent)
    {
        grid.GetAdjacent(adjacent, location);
        for (var i = 0; i < adjacent.Length; i++)
        {
            if (adjacent[i].IsValid && grid.IsEmpty(adjacent[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAdjacentToNetwork(SquareGrid grid, ILocationSet chosen, Location candidate, Span<Location> adjacent)
    {
        grid.GetAdjacent(adjacent, candidate);
        for (var i = 0; i < adjacent.Length; i++)
        {
            if (adjacent[i].IsValid && chosen.Contains(adjacent[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddTile(
        ILocationDictionary<List<Location>> coveredPipes,
        ILocationDictionary<List<Location>> coveredCenters,
        ILocationSet chosen,
        Location location,
        ILocationSet uncoveredPipes,
        ILocationSet uncoveredCenters)
    {
        if (!chosen.Add(location))
        {
            return;
        }

        RemoveCovered(coveredPipes, location, uncoveredPipes);
        RemoveCovered(coveredCenters, location, uncoveredCenters);
    }

    private static void AddCoverage(ILocationDictionary<List<Location>> covered, List<Location> candidates, Location candidate, Location target)
    {
        if (!covered.TryGetValue(candidate, out var targets))
        {
            targets = new List<Location>();
            covered.Add(candidate, targets);
            candidates.Add(candidate);
        }

        targets.Add(target);
    }

    private static int CountCovered(ILocationDictionary<List<Location>> covered, Location candidate, ILocationSet uncovered)
    {
        if (!covered.TryGetValue(candidate, out var targets))
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            if (uncovered.Contains(targets[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static void RemoveCovered(ILocationDictionary<List<Location>> covered, Location candidate, ILocationSet uncovered)
    {
        if (!covered.TryGetValue(candidate, out var targets))
        {
            return;
        }

        for (var i = 0; i < targets.Count; i++)
        {
            uncovered.Remove(targets[i]);
        }
    }

    /// <summary>
    /// Finds the shortest path of empty tiles from the current network (any chosen tile) to <paramref name="goal"/>,
    /// inclusive of both ends. Returns null if the goal cannot be reached through empty tiles. Chosen tiles are still
    /// empty at this stage (placement is deferred), so the search can travel along the existing network.
    /// </summary>
    private static List<Location>? BridgeToTile(Context context, ILocationSet chosen, Location goal)
    {
        var grid = context.Grid;
        var queue = new Queue<Location>();
        var visited = context.GetLocationSet();
        var parents = context.GetLocationDictionary<Location>();

        foreach (var location in chosen.EnumerateItems())
        {
            queue.Enqueue(location);
        }

#if USE_STACKALLOC && LOCATION_AS_STRUCT
        Span<Location> neighbors = stackalloc Location[4];
#else
        Span<Location> neighbors = new Location[4];
#endif

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current == goal)
            {
                var path = new List<Location> { current };
                while (parents.TryGetValue(current, out var parent))
                {
                    path.Add(parent);
                    current = parent;
                }

                return path;
            }

            grid.GetNeighbors(neighbors, current);
            for (var i = 0; i < neighbors.Length; i++)
            {
                var next = neighbors[i];
                if (!next.IsValid || visited.Contains(next) || !parents.TryAdd(next, current))
                {
                    continue;
                }

                queue.Enqueue(next);
            }
        }

        return null;
    }
}
