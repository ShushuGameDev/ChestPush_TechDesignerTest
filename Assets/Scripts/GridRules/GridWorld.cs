using System;
using System.Collections.Generic;
using System.Linq;
using ChestPush.LevelData;

namespace ChestPush.GridRules
{
    public enum GridDirection { North, East, South, West }

    public static class GridDirections
    {
        public static GridPoint Step(GridPoint point, GridDirection direction)
        {
            switch (direction)
            {
                case GridDirection.North: return point.Offset(0, 1);
                case GridDirection.East: return point.Offset(1, 0);
                case GridDirection.South: return point.Offset(0, -1);
                default: return point.Offset(-1, 0);
            }
        }

        public static GridDirection Clockwise(GridDirection direction) => (GridDirection)(((int)direction + 1) % 4);
    }

    public sealed class WorldSnapshot
    {
        public GridPoint player;
        public GridDirection facing;
        public Dictionary<string, GridPoint> boxes = new Dictionary<string, GridPoint>();
        public HashSet<string> destroyedWalls = new HashSet<string>();
        public int actionCount;
        public bool completed;

        public WorldSnapshot Copy() => new WorldSnapshot
        {
            player = player,
            facing = facing,
            boxes = new Dictionary<string, GridPoint>(boxes),
            destroyedWalls = new HashSet<string>(destroyedWalls),
            actionCount = actionCount,
            completed = completed
        };
    }

    public sealed class GridWorld
    {
        private readonly BlockCatalog catalog;
        private readonly HashSet<GridPoint> floors = new HashSet<GridPoint>();
        private readonly HashSet<GridPoint> targets = new HashSet<GridPoint>();
        private readonly Dictionary<GridPoint, Placement> structures = new Dictionary<GridPoint, Placement>();
        private readonly Dictionary<GridPoint, List<Placement>> markers = new Dictionary<GridPoint, List<Placement>>();
        private readonly List<Placement> breakableWalls = new List<Placement>();
        private readonly Stack<WorldSnapshot> history = new Stack<WorldSnapshot>();
        private readonly WorldSnapshot initial;
        private WorldSnapshot state;

        public GridPoint Player => state.player;
        public GridDirection Facing => state.facing;
        public IReadOnlyDictionary<string, GridPoint> Boxes => state.boxes;
        public IReadOnlyCollection<string> DestroyedWalls => state.destroyedWalls;
        public bool Completed => state.completed;
        public int ActionCount => state.actionCount;
        public int UndoCount => history.Count;

        public GridWorld(LevelDefinition level, BlockCatalog catalog)
        {
            var validation = LevelValidator.ValidateLevel(level, catalog);
            if (!validation.IsValid) throw new ArgumentException(string.Join("\n", validation.issues));
            this.catalog = catalog;
            foreach (var layer in level.layers)
                foreach (var floor in layer.floors)
                    floors.Add(new GridPoint(layer.id, floor.x, floor.z));
            state = new WorldSnapshot();
            foreach (var placement in level.placements)
            {
                var entry = catalog.Find(placement.blockId);
                switch (placement.slot)
                {
                    case PlacementSlot.Structure:
                        structures.Add(placement.Point, placement);
                        if (entry.kind == BlockKind.Breakable) breakableWalls.Add(placement);
                        break;
                    case PlacementSlot.Marker:
                        if (!markers.TryGetValue(placement.Point, out var list)) markers.Add(placement.Point, list = new List<Placement>());
                        list.Add(placement);
                        if (entry.kind == BlockKind.Spawn) state.player = placement.Point;
                        if (entry.kind == BlockKind.Target) targets.Add(placement.Point);
                        break;
                    case PlacementSlot.Box:
                        state.boxes.Add(placement.instanceId, placement.Point);
                        break;
                }
            }
            foreach (var box in state.boxes.Values.ToArray()) TriggerPressurePlate(box);
            state.completed = CheckCompleted();
            initial = state.Copy();
        }

        public WorldSnapshot Capture() => state.Copy();

        public bool TryMove(GridDirection direction)
        {
            var before = state.Copy();
            state.facing = direction;
            var next = GridDirections.Step(state.player, direction);
            bool success;
            var boxId = BoxAt(next);
            if (boxId != null)
            {
                var boxDestination = GridDirections.Step(next, direction);
                success = TryPushBox(boxId, boxDestination, direction) && TryLandPlayer(next, direction);
            }
            else success = TryLandPlayer(next, direction);
            if (!success) { state = before; state.facing = direction; return false; }
            state.actionCount++;
            state.completed = CheckCompleted();
            history.Push(before);
            return true;
        }

        public bool TryInteract()
        {
            var target = GridDirections.Step(state.player, state.facing);
            if (!structures.TryGetValue(target, out var wall) || !IsBreakable(wall) ||
                state.destroyedWalls.Contains(wall.instanceId) || (wall.breakModes & BreakMode.Interact) == 0)
                return false;
            history.Push(state.Copy());
            state.destroyedWalls.Add(wall.instanceId);
            state.actionCount++;
            return true;
        }

        public bool Undo()
        {
            if (history.Count == 0) return false;
            state = history.Pop();
            return true;
        }

        public void Restart()
        {
            state = initial.Copy();
            history.Clear();
        }

        private bool TryPushBox(string boxId, GridPoint destination, GridDirection direction)
        {
            if (!floors.Contains(destination) || BoxAt(destination) != null || destination.Equals(state.player)) return false;
            if (structures.TryGetValue(destination, out var wall) && !state.destroyedWalls.Contains(wall.instanceId))
            {
                if (IsBreakable(wall) && (wall.breakModes & BreakMode.BoxImpact) != 0)
                    state.destroyedWalls.Add(wall.instanceId);
                else if (!CanEnter(destination, ActorMask.Box)) return false;
            }
            if (TryGetTeleport(destination, out var portal))
            {
                if ((portal.allowedActors & ActorMask.Box) == 0 || !portal.destination.HasValue) return false;
                var exit = portal.destination.Value;
                if (!CanStand(exit) || BoxAt(exit) != null || exit.Equals(state.player)) return false;
                destination = exit;
            }
            state.boxes[boxId] = destination;
            TriggerPressurePlate(destination);
            return true;
        }

        private bool TryLandPlayer(GridPoint destination, GridDirection direction)
        {
            if (!CanEnter(destination, ActorMask.Player) || BoxAt(destination) != null) return false;
            if (TryGetTeleport(destination, out var portal))
            {
                if ((portal.allowedActors & ActorMask.Player) == 0 || !portal.destination.HasValue) return false;
                var exit = portal.destination.Value;
                if (!CanStand(exit)) return false;
                var boxId = BoxAt(exit);
                if (boxId != null && !TryShoveBox(boxId, exit, direction)) return false;
                destination = exit;
            }
            state.player = destination;
            return true;
        }

        private bool TryShoveBox(string boxId, GridPoint exit, GridDirection direction)
        {
            var candidateDirection = direction;
            for (int i = 0; i < 4; i++)
            {
                var candidate = GridDirections.Step(exit, candidateDirection);
                if (CanStand(candidate) && BoxAt(candidate) == null && !candidate.Equals(state.player))
                {
                    state.boxes[boxId] = candidate;
                    TriggerPressurePlate(candidate);
                    return true;
                }
                candidateDirection = GridDirections.Clockwise(candidateDirection);
            }
            return false;
        }

        private bool CanStand(GridPoint point)
        {
            if (!floors.Contains(point)) return false;
            if (!structures.TryGetValue(point, out var structure) || state.destroyedWalls.Contains(structure.instanceId)) return true;
            return catalog.Find(structure.blockId).passable;
        }

        private bool CanEnter(GridPoint point, ActorMask actor)
        {
            if (!CanStand(point)) return false;
            return !TryGetTeleport(point, out var portal) || (portal.allowedActors & actor) != 0;
        }

        private bool TryGetTeleport(GridPoint point, out Placement portal)
        {
            if (structures.TryGetValue(point, out portal) && catalog.Find(portal.blockId).kind == BlockKind.Teleport) return true;
            portal = null;
            return false;
        }

        private bool IsBreakable(Placement placement) => catalog.Find(placement.blockId).kind == BlockKind.Breakable;

        private string BoxAt(GridPoint point)
        {
            foreach (var box in state.boxes) if (box.Value.Equals(point)) return box.Key;
            return null;
        }

        private void TriggerPressurePlate(GridPoint point)
        {
            if (!markers.TryGetValue(point, out var list)) return;
            foreach (var plate in list)
            {
                if (catalog.Find(plate.blockId).kind != BlockKind.PressurePlate) continue;
                foreach (var wall in breakableWalls)
                    if ((wall.breakModes & BreakMode.Switch) != 0 && wall.switchIds != null && wall.switchIds.Contains(plate.switchId))
                        state.destroyedWalls.Add(wall.instanceId);
            }
        }

        private bool CheckCompleted() => targets.Count > 0 && targets.All(target => BoxAt(target) != null);
    }
}
