using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ChestPush.LevelData
{
    public enum BlockCategory { Floor, Structure, Marker, Box, Player }
    public enum BlockKind { Normal, Breakable, Teleport, Spawn, Target, PressurePlate }
    public enum PlacementSlot { Floor, Structure, Marker, Box }
    public enum UnlockMode { Any, All }

    [Flags]
    public enum ActorMask { None = 0, Player = 1, Box = 2, Both = Player | Box }

    [Flags]
    public enum BreakMode { None = 0, Interact = 1, BoxImpact = 2, Switch = 4 }

    [Serializable]
    public struct GridPoint : IEquatable<GridPoint>
    {
        public int layerId;
        public int x;
        public int z;

        public GridPoint(int layerId, int x, int z)
        {
            this.layerId = layerId;
            this.x = x;
            this.z = z;
        }

        public GridPoint Offset(int dx, int dz) => new GridPoint(layerId, x + dx, z + dz);
        public bool Equals(GridPoint other) => layerId == other.layerId && x == other.x && z == other.z;
        public override bool Equals(object obj) => obj is GridPoint other && Equals(other);
        public override int GetHashCode() { unchecked { return ((layerId * 397) ^ x) * 397 ^ z; } }
        public override string ToString() => $"{layerId}:{x},{z}";
    }

    [Serializable]
    public sealed class FloorCell
    {
        public int x;
        public int z;
        public string blockId = "Floor_Normal";
    }

    [Serializable]
    public sealed class FloorLayer
    {
        public int id;
        public int baseY;
        public List<FloorCell> floors = new List<FloorCell>();
    }

    [Serializable]
    public sealed class Placement
    {
        public string instanceId = Guid.NewGuid().ToString("N");
        public int layerId;
        public int x;
        public int z;
        public PlacementSlot slot;
        public string blockId;
        public BreakMode breakModes;
        public List<string> switchIds = new List<string>();
        public string switchId;
        public GridPoint? destination;
        public ActorMask allowedActors = ActorMask.Both;

        public GridPoint Point => new GridPoint(layerId, x, z);
    }

    [Serializable]
    public sealed class LevelDefinition
    {
        public int schemaVersion = 1;
        public string levelId = Guid.NewGuid().ToString("N");
        public List<FloorLayer> layers = new List<FloorLayer> { new FloorLayer { id = 0, baseY = 0 } };
        public List<Placement> placements = new List<Placement>();
    }

    [Serializable]
    public sealed class LevelGraphNode
    {
        public string levelId;
        public string address;
        public int selectOrder;
        public UnlockMode unlockMode = UnlockMode.Any;
        public float editorX;
        public float editorY;
        public List<string> successors = new List<string>();
    }

    [Serializable]
    public sealed class LevelGraphDefinition
    {
        public int schemaVersion = 1;
        public List<string> entryLevelIds = new List<string>();
        public List<LevelGraphNode> nodes = new List<LevelGraphNode>();
    }

    [Serializable]
    public sealed class BlockEntry
    {
        public string id;
        public BlockCategory category;
        public BlockKind kind;
        public bool passable;
        public string address;
        public AssetReferenceGameObject prefab;
    }

    public static class LevelPaths
    {
        public const string GraphAssetPath = "Assets/Levels/LevelGraph.json";
        public const string CatalogAssetPath = "Assets/Data/BlockCatalog.asset";
        public const string GraphAddress = "ChestPush/LevelGraph";
        public const string CatalogAddress = "ChestPush/BlockCatalog";
        public const string LevelAddressPrefix = "ChestPush/Levels/";
        public const string VictoryEffectAssetPath = "Assets/Prefabs/Effects/VictoryBurstGreen.prefab";
        public const string VictoryEffectAddress = "ChestPush/Effects/VictoryBurstGreen";
    }
}
