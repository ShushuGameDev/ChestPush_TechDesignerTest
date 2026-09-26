using System;
using System.Collections.Generic;
using ChestPush.GridRules;
using ChestPush.LevelData;
using NUnit.Framework;
using UnityEngine;

namespace ChestPush.Tests
{
    public sealed class LevelAndRulesTests
    {
        private BlockCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<BlockCatalog>();
            Add("Floor_Normal", BlockCategory.Floor, BlockKind.Normal, true);
            Add("Wall_Normal", BlockCategory.Structure, BlockKind.Normal, false);
            Add("Wall_Breakable", BlockCategory.Structure, BlockKind.Breakable, false);
            Add("Wall_Teleport", BlockCategory.Structure, BlockKind.Teleport, true);
            Add("Box_Normal", BlockCategory.Box, BlockKind.Normal, false);
            Add("Marker_Spawn", BlockCategory.Marker, BlockKind.Spawn, true);
            Add("Marker_Target", BlockCategory.Marker, BlockKind.Target, true);
            Add("Marker_PressurePlate", BlockCategory.Marker, BlockKind.PressurePlate, true);
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(catalog);

        [Test]
        public void LevelJsonRoundTripKeepsStableIdsAndConfig()
        {
            var level = Line(0, 3);
            var wall = Place(level, "Wall_Breakable", PlacementSlot.Structure, 2, 0);
            wall.breakModes = BreakMode.Interact | BreakMode.BoxImpact;
            var parsed = LevelCodec.ReadLevel(LevelCodec.WriteLevel(level));
            Assert.AreEqual(level.levelId, parsed.levelId);
            Assert.AreEqual(1, parsed.layers.Count);
            Assert.AreEqual(wall.instanceId, parsed.placements[0].instanceId);
            Assert.AreEqual(wall.breakModes, parsed.placements[0].breakModes);
        }

        [Test]
        public void PushToGoalAndUndoRestoresPlayerAndBox()
        {
            var level = Line(0, 3);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 0);
            Place(level, "Marker_Target", PlacementSlot.Marker, 3, 0);
            var world = new GridWorld(level, catalog);
            Assert.IsTrue(world.TryMove(GridDirection.East));
            Assert.IsTrue(world.TryMove(GridDirection.East));
            Assert.IsTrue(world.Completed);
            Assert.IsTrue(world.Undo());
            Assert.IsFalse(world.Completed);
            Assert.AreEqual(new GridPoint(0, 1, 0), world.Player);
            Assert.IsTrue(world.Undo());
            Assert.AreEqual(new GridPoint(0, 0, 0), world.Player);
        }

        [Test]
        public void BoxImpactAndInteractionBreakWallAndUndo()
        {
            var level = Line(0, 4);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 0);
            Place(level, "Marker_Target", PlacementSlot.Marker, 4, 0);
            var wall = Place(level, "Wall_Breakable", PlacementSlot.Structure, 2, 0);
            wall.breakModes = BreakMode.BoxImpact | BreakMode.Interact;
            var world = new GridWorld(level, catalog);
            Assert.IsTrue(world.TryMove(GridDirection.East));
            CollectionAssert.Contains(world.DestroyedWalls, wall.instanceId);
            Assert.IsTrue(world.Undo());
            CollectionAssert.DoesNotContain(world.DestroyedWalls, wall.instanceId);

            level.placements.RemoveAll(item => item.slot == PlacementSlot.Box);
            Place(level, "Box_Normal", PlacementSlot.Box, 4, 0);
            level.placements.RemoveAll(item => item.blockId == "Marker_Spawn");
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 1, 0);
            world = new GridWorld(level, catalog);
            Assert.IsFalse(world.TryMove(GridDirection.East));
            Assert.IsTrue(world.TryInteract());
            CollectionAssert.Contains(world.DestroyedWalls, wall.instanceId);
            Assert.IsTrue(world.Undo());
            CollectionAssert.DoesNotContain(world.DestroyedWalls, wall.instanceId);
        }

        [Test]
        public void BoxPressurePlatePermanentlyBreaksLinkedWallWithinTurn()
        {
            var level = Line(0, 4);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 0);
            Place(level, "Marker_Target", PlacementSlot.Marker, 4, 0);
            var plate = Place(level, "Marker_PressurePlate", PlacementSlot.Marker, 2, 0);
            plate.switchId = "red";
            var wall = Place(level, "Wall_Breakable", PlacementSlot.Structure, 3, 0);
            wall.breakModes = BreakMode.Switch;
            wall.switchIds.Add("red");
            var world = new GridWorld(level, catalog);
            Assert.IsTrue(world.TryMove(GridDirection.East));
            CollectionAssert.Contains(world.DestroyedWalls, wall.instanceId);
            world.Undo();
            CollectionAssert.DoesNotContain(world.DestroyedWalls, wall.instanceId);
        }

        [Test]
        public void TeleportShovesBoxForwardThenClockwise()
        {
            var level = new LevelDefinition();
            Floor(level, 0, 0, 0); Floor(level, 0, 0, 1);
            level.layers.Add(new FloorLayer { id = 1, baseY = 2 });
            Floor(level, 1, 1, 1); Floor(level, 1, 2, 1);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            var portal = Place(level, "Wall_Teleport", PlacementSlot.Structure, 0, 1);
            portal.destination = new GridPoint(1, 1, 1);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 1, 1);
            Place(level, "Marker_Target", PlacementSlot.Marker, 2, 1, 1);
            var world = new GridWorld(level, catalog);
            Assert.IsTrue(world.TryMove(GridDirection.North));
            Assert.AreEqual(new GridPoint(1, 1, 1), world.Player);
            Assert.AreEqual(new GridPoint(1, 2, 1), new List<GridPoint>(world.Boxes.Values)[0]);
            Assert.IsTrue(world.Completed);
            world.Undo();
            Assert.AreEqual(new GridPoint(0, 0, 0), world.Player);
        }

        [Test]
        public void AnyAndAllUnlockPoliciesUseIncomingEdges()
        {
            var graph = new LevelGraphDefinition();
            LevelGraphNode a = Node(), b = Node(), c = Node();
            graph.nodes.AddRange(new[] { a, b, c });
            graph.entryLevelIds.AddRange(new[] { a.levelId, b.levelId });
            a.successors.Add(c.levelId); b.successors.Add(c.levelId);
            c.unlockMode = UnlockMode.All;
            Assert.IsFalse(GraphProgression.IsUnlocked(graph, new[] { a.levelId }, c.levelId));
            Assert.IsTrue(GraphProgression.IsUnlocked(graph, new[] { a.levelId, b.levelId }, c.levelId));
            c.unlockMode = UnlockMode.Any;
            Assert.IsTrue(GraphProgression.IsUnlocked(graph, new[] { a.levelId }, c.levelId));
        }

        [Test]
        public void ValidatorReportsMissingLayer()
        {
            var level = Line(0, 2);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 0, 99);
            Place(level, "Marker_Target", PlacementSlot.Marker, 2, 0);
            Assert.IsTrue(LevelValidator.ValidateLevel(level, catalog).issues.Exists(issue => issue.code == "MissingLayer"));
        }

        [Test]
        public void DraftTeleportMayWaitForExitButPlayableLevelCannot()
        {
            var level = Line(0, 1);
            Place(level, "Wall_Teleport", PlacementSlot.Structure, 1, 0);
            Assert.IsTrue(LevelValidator.ValidateLevel(level, catalog, false).IsValid);
            Assert.IsTrue(LevelValidator.ValidateLevel(level, catalog, true).issues.Exists(issue => issue.code == "TeleportDestination"));
        }

        [Test]
        public void ValidatorRejectsBoxAndSpawnInsideFixedWall()
        {
            var level = Line(0, 2);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            Place(level, "Wall_Normal", PlacementSlot.Structure, 0, 0);
            Place(level, "Wall_Normal", PlacementSlot.Structure, 1, 0);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 0);
            Place(level, "Marker_Target", PlacementSlot.Marker, 2, 0);
            var issues = LevelValidator.ValidateLevel(level, catalog).issues;
            Assert.IsTrue(issues.Exists(issue => issue.code == "SpawnBlocked"));
            Assert.IsTrue(issues.Exists(issue => issue.code == "BoxBlocked"));
        }

        [Test]
        public void ValidatorRejectsTargetSharingBoxCellInDraftAndPlayableLevel()
        {
            var level = Line(0, 2);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 0);
            Place(level, "Marker_Target", PlacementSlot.Marker, 1, 0);

            Assert.IsTrue(LevelValidator.ValidateLevel(level, catalog, false).issues.Exists(issue =>
                issue.code == "TargetBoxOverlap" && issue.location == "0:1,0"));
            Assert.IsTrue(LevelValidator.ValidateLevel(level, catalog, true).issues.Exists(issue =>
                issue.code == "TargetBoxOverlap"));
        }

        [Test]
        public void BoxOnlyTeleportMovesBoxAcrossLayers()
        {
            var level = Line(0, 2);
            level.layers.Add(new FloorLayer { id = 1, baseY = 2 });
            Floor(level, 1, 0, 0);
            Place(level, "Marker_Spawn", PlacementSlot.Marker, 0, 0);
            Place(level, "Box_Normal", PlacementSlot.Box, 1, 0);
            var portal = Place(level, "Wall_Teleport", PlacementSlot.Structure, 2, 0);
            portal.allowedActors = ActorMask.Box;
            portal.destination = new GridPoint(1, 0, 0);
            Place(level, "Marker_Target", PlacementSlot.Marker, 0, 0, 1);
            var world = new GridWorld(level, catalog);
            Assert.IsTrue(world.TryMove(GridDirection.East));
            Assert.AreEqual(new GridPoint(0, 1, 0), world.Player);
            Assert.AreEqual(new GridPoint(1, 0, 0), new List<GridPoint>(world.Boxes.Values)[0]);
            Assert.IsTrue(world.Completed);
        }

        private void Add(string id, BlockCategory category, BlockKind kind, bool passable) =>
            catalog.entries.Add(new BlockEntry { id = id, category = category, kind = kind, passable = passable, address = id });

        private static LevelDefinition Line(int from, int to)
        {
            var level = new LevelDefinition();
            for (int x = from; x <= to; x++) Floor(level, 0, x, 0);
            return level;
        }

        private static void Floor(LevelDefinition level, int layerId, int x, int z) =>
            level.layers.Find(layer => layer.id == layerId).floors.Add(new FloorCell { x = x, z = z });

        private static Placement Place(LevelDefinition level, string id, PlacementSlot slot, int x, int z, int layerId = 0)
        {
            var placement = new Placement { blockId = id, slot = slot, x = x, z = z, layerId = layerId };
            level.placements.Add(placement);
            return placement;
        }

        private static LevelGraphNode Node() => new LevelGraphNode
        {
            levelId = Guid.NewGuid().ToString("N"), address = "test", selectOrder = 0
        };
    }
}
