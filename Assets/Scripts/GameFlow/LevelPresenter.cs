using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ChestPush.GridRules;
using ChestPush.LevelData;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ChestPush.GameFlow
{
    public sealed class LevelPresenter
    {
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> prefabHandles = new Dictionary<string, AsyncOperationHandle<GameObject>>();
        private readonly Dictionary<string, GameObject> placementObjects = new Dictionary<string, GameObject>();
        private readonly Dictionary<int, int> layerHeights = new Dictionary<int, int>();
        private Transform root;
        private GameObject playerObject;
        private Vector3 cameraPivot;
        private bool hasCameraPivot;
        private float cameraYawDegrees = 45f;

        public IEnumerator Build(LevelDefinition level, BlockCatalog catalog, WorldSnapshot state)
        {
            Clear();
            root = new GameObject("Generated Level").transform;
            foreach (var layer in level.layers) layerHeights[layer.id] = layer.baseY;
            var ids = new HashSet<string>(level.layers.SelectMany(l => l.floors).Select(f => f.blockId));
            foreach (var placement in level.placements) ids.Add(placement.blockId);
            ids.Add("Player_Normal");
            foreach (var id in ids)
            {
                var entry = catalog.Find(id);
                if (entry == null) { Debug.LogError($"Missing block {id}"); continue; }
                if (prefabHandles.ContainsKey(entry.address)) continue;
                var handle = Addressables.LoadAssetAsync<GameObject>(entry.address);
                yield return handle;
                if (handle.Status != AsyncOperationStatus.Succeeded) Debug.LogError($"Cannot load {entry.address}");
                else prefabHandles.Add(entry.address, handle);
            }
            foreach (var layer in level.layers)
                foreach (var floor in layer.floors)
                    Create(catalog, floor.blockId, new GridPoint(layer.id, floor.x, floor.z), PlacementSlot.Floor, $"Floor {layer.id}:{floor.x},{floor.z}");
            foreach (var placement in level.placements)
            {
                var instance = Create(catalog, placement.blockId, placement.Point, placement.slot, placement.blockId);
                if (instance != null) placementObjects[placement.instanceId] = instance;
            }
            playerObject = Create(catalog, "Player_Normal", state.player, PlacementSlot.Box, "Player");
            Apply(state);
            FrameCamera(level);
        }

        public IEnumerator Animate(WorldSnapshot before, WorldSnapshot after, float duration = 0.14f)
        {
            var starts = new Dictionary<string, Vector3>();
            var ends = new Dictionary<string, Vector3>();
            foreach (var box in after.boxes)
            {
                if (!placementObjects.TryGetValue(box.Key, out var instance)) continue;
                starts[box.Key] = instance.transform.position;
                ends[box.Key] = WorldPosition(box.Value, PlacementSlot.Box);
            }
            Vector3 playerStart = playerObject == null ? Vector3.zero : playerObject.transform.position;
            Vector3 playerEnd = WorldPosition(after.player, PlacementSlot.Box);
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
                if (playerObject != null) playerObject.transform.position = Vector3.Lerp(playerStart, playerEnd, t);
                foreach (var box in ends) placementObjects[box.Key].transform.position = Vector3.Lerp(starts[box.Key], box.Value, t);
                yield return null;
            }
            Apply(after);
        }

        public void Apply(WorldSnapshot state)
        {
            foreach (var wallId in state.destroyedWalls)
                if (placementObjects.TryGetValue(wallId, out var wall)) wall.SetActive(false);
            foreach (var pair in placementObjects)
                if (pair.Value != null && pair.Value.activeSelf == false && !state.destroyedWalls.Contains(pair.Key)) pair.Value.SetActive(true);
            foreach (var box in state.boxes)
                if (placementObjects.TryGetValue(box.Key, out var instance)) instance.transform.position = WorldPosition(box.Value, PlacementSlot.Box);
            if (playerObject != null) playerObject.transform.position = WorldPosition(state.player, PlacementSlot.Box);
        }

        public IEnumerator PlayVictoryEffect(GameObject prefab, WorldSnapshot state)
        {
            if (prefab == null || root == null) yield break;
            var effects = new List<GameObject>();
            foreach (var point in state.boxes.Values.Distinct())
            {
                var position = WorldPosition(point, PlacementSlot.Box) + Vector3.up * 0.65f;
                var effect = Object.Instantiate(prefab, position, Quaternion.identity, root);
                effects.Add(effect);
                foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true)) particle.Play(true);
            }
            bool alive;
            do
            {
                alive = false;
                foreach (var effect in effects)
                {
                    if (effect != null && effect.GetComponentsInChildren<ParticleSystem>(true).Any(particle => particle.IsAlive(true)))
                    {
                        alive = true;
                        break;
                    }
                }
                if (alive) yield return null;
            } while (alive);
            foreach (var effect in effects) if (effect != null) Object.Destroy(effect);
        }

        public GridDirection ToWorldDirection(GridDirection viewDirection)
        {
            return RotateViewDirection(viewDirection, cameraYawDegrees);
        }

        public bool TryGetDirectionHintPosition(GridDirection viewDirection, out Vector3 position)
        {
            if (playerObject == null)
            {
                position = Vector3.zero;
                return false;
            }
            var camera = Camera.main;
            float hintYawDegrees = camera == null ? cameraYawDegrees :
                Mathf.Repeat(Mathf.Atan2(camera.transform.forward.x, camera.transform.forward.z) * Mathf.Rad2Deg, 360f);
            Vector3 offset;
            switch (RotateViewDirection(viewDirection, hintYawDegrees))
            {
                case GridDirection.North: offset = Vector3.forward; break;
                case GridDirection.East: offset = Vector3.right; break;
                case GridDirection.South: offset = Vector3.back; break;
                default: offset = Vector3.left; break;
            }
            position = playerObject.transform.position + offset * 0.9f + Vector3.up * 0.65f;
            return true;
        }

        private static GridDirection RotateViewDirection(GridDirection viewDirection, float yawDegrees)
        {
            int quarterTurns = Mathf.FloorToInt(Mathf.Repeat(yawDegrees + 0.01f, 360f) / 90f);
            return (GridDirection)(((int)viewDirection + quarterTurns) % 4);
        }

        public IEnumerator RotateCamera(float yawDegrees, float duration = 0.22f)
        {
            var camera = Camera.main;
            if (camera == null || !hasCameraPivot) yield break;
            var rotation = Quaternion.AngleAxis(yawDegrees, Vector3.up);
            var startPosition = camera.transform.position;
            var startOffset = startPosition - cameraPivot;
            var startRotation = camera.transform.rotation;
            var targetPosition = cameraPivot + rotation * (startPosition - cameraPivot);
            var targetRotation = rotation * startRotation;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                camera.transform.position = cameraPivot + Quaternion.Slerp(Quaternion.identity, rotation, t) * startOffset;
                camera.transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
                yield return null;
            }
            camera.transform.position = targetPosition;
            camera.transform.rotation = targetRotation;
            cameraYawDegrees = Mathf.Repeat(cameraYawDegrees + yawDegrees, 360f);
        }

        public void Clear()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            playerObject = null;
            hasCameraPivot = false;
            cameraYawDegrees = 45f;
            placementObjects.Clear();
            layerHeights.Clear();
            foreach (var handle in prefabHandles.Values) if (handle.IsValid()) Addressables.Release(handle);
            prefabHandles.Clear();
        }

        private GameObject Create(BlockCatalog catalog, string id, GridPoint point, PlacementSlot slot, string name)
        {
            var entry = catalog.Find(id);
            if (entry == null || !prefabHandles.TryGetValue(entry.address, out var handle) || handle.Result == null) return null;
            var instance = Object.Instantiate(handle.Result, WorldPosition(point, slot), Quaternion.identity, root);
            instance.name = name;
            return instance;
        }

        private Vector3 WorldPosition(GridPoint point, PlacementSlot slot)
        {
            int y = layerHeights.TryGetValue(point.layerId, out var height) ? height : 0;
            float centerY = slot == PlacementSlot.Floor ? y + 0.5f : slot == PlacementSlot.Marker ? y + 1.04f : y + 1.5f;
            return new Vector3(point.x + 0.5f, centerY, point.z + 0.5f);
        }

        private void FrameCamera(LevelDefinition level)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var cells = level.layers.SelectMany(l => l.floors.Select(f => new Vector3(f.x + 0.5f, l.baseY + 1, f.z + 0.5f))).ToArray();
            if (cells.Length == 0) return;
            float minX = cells.Min(p => p.x), maxX = cells.Max(p => p.x);
            float minZ = cells.Min(p => p.z), maxZ = cells.Max(p => p.z);
            float minY = cells.Min(p => p.y), maxY = cells.Max(p => p.y);
            var center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            cameraPivot = center;
            hasCameraPivot = true;
            cameraYawDegrees = 45f;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(5, Mathf.Max(maxX - minX, maxZ - minZ) * 0.9f + 3);
            camera.transform.rotation = Quaternion.Euler(45, 45, 0);
            camera.transform.position = center - camera.transform.forward * 20;
        }
    }
}
