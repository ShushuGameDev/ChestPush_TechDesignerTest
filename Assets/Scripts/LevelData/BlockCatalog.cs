using System.Collections.Generic;
using UnityEngine;

namespace ChestPush.LevelData
{
    [CreateAssetMenu(menuName = "ChestPush/Block Catalog", fileName = "BlockCatalog")]
    public sealed class BlockCatalog : ScriptableObject
    {
        public List<BlockEntry> entries = new List<BlockEntry>();

        public BlockEntry Find(string id) => entries == null ? null : entries.Find(entry => entry != null && entry.id == id);
    }
}
