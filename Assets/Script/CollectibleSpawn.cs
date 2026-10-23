using System.Collections.Generic;
using UnityEngine;

// Spawns coins and fuel on already-generated Sprite Shape terrain.
// Adaptive difficulty can reduce the probability of future coin spawns.
// Does not change terrain generation, driving, pickup values or game-over behaviour.
public class CollectibleSpawn : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the vehicle you drive from the scene Hierarchy.")]
    [SerializeField] private Transform player;

    [Tooltip("Drag the Edge Collider 2D component on Ground.")]
    [SerializeField] private EdgeCollider2D groundCollider;

    [Header("Coin Prefabs")]
    [Tooltip("Existing collectible prefabs; keep COINS in this array, not fuel cans.")]
    [SerializeField] private GameObject[] collectiblePrefabs;

    [Header("Fuel Prefab")]
    [Tooltip("A fuel-canister PREFAB with its existing CollectFuel behaviour.")]
    [SerializeField] private GameObject fuelPrefab;

    [Header("Spacing (world units)")]
    [SerializeField, Min(2f)] private float coinSpacing = 10f;
    [SerializeField, Min(10f)] private float fuelSpacing = 55f;
    [SerializeField, Min(5f)] private float spawnAhead = 35f;
    [SerializeField, Min(5f)] private float removeBehind = 45f;

    [Header("Height Above Ground (world units)")]
    [SerializeField, Min(0.1f)] private float coinHeight = 1.2f;
    [SerializeField, Min(0.1f)] private float fuelHeight = 1.5f;

    [Header("Algorithm 5 - Adaptive Coin Scarcity")]
    [Tooltip("Off = original behaviour: a coin at every valid coin spawn position.")]
    [SerializeField] private bool enableAdaptiveCoinScarcity = false;

    [Tooltip("Coin spawn chance stays at 100% for the first part of the course.")]
    [SerializeField, Min(0f)] private float adaptiveStartDistance = 200f;

    [Tooltip("Distance between each additional difficulty step.")]
    [SerializeField, Min(1f)] private float distancePerDifficultyStep = 175f;

    [Tooltip("Probability removed at each step: 0.10 means 10 percentage points.")]
    [SerializeField, Range(0f, 0.5f)] private float chanceReductionPerStep = 0.10f;

    [Tooltip("Coin spawn chance never falls below this value.")]
    [SerializeField, Range(0.1f, 1f)] private float minimumCoinSpawnChance = 0.60f;

    private float startingX;
    private float nextCoinX;
    private float nextFuelX;

    private struct SpawnedPickup
    {
        public float x;
        public GameObject instance;

        public SpawnedPickup(float x, GameObject instance)
        {
            this.x = x;
            this.instance = instance;
        }
    }

    // Coin/fuel batches are not globally sorted by X, so a FIFO can delay cleanup.
    private readonly List<SpawnedPickup> spawned = new List<SpawnedPickup>(32);
    private readonly List<RaycastHit2D> groundHits = new List<RaycastHit2D>(16);
    private ContactFilter2D groundFilter;
    private GameObject[] validCoinPrefabs;

    private void Start()
    {
        if (player == null || groundCollider == null)
        {
            Debug.LogError("CollectibleSpawn: assign Player and Ground Collider in the Inspector.", this);
            enabled = false;
            return;
        }

        if (collectiblePrefabs == null || collectiblePrefabs.Length == 0)
            Debug.LogWarning("CollectibleSpawn: no coin prefabs assigned.", this);

        if (fuelPrefab == null)
            Debug.LogWarning("CollectibleSpawn: no fuel prefab assigned; fuel will not spawn.", this);

        var valid = new List<GameObject>();
        if (collectiblePrefabs != null)
            foreach (GameObject prefab in collectiblePrefabs)
                if (prefab != null) valid.Add(prefab);
        validCoinPrefabs = valid.ToArray();
        groundFilter = new ContactFilter2D { useTriggers = false };
        groundFilter.SetLayerMask(1 << groundCollider.gameObject.layer);
        coinSpacing = Mathf.Max(2f, coinSpacing);
        fuelSpacing = Mathf.Max(10f, fuelSpacing);

        startingX = player.position.x;

        // Begin ahead of the vehicle so pickups don't appear inside the chassis.
        nextCoinX = startingX + 12f;
        nextFuelX = startingX + 30f;
    }

    private void Update()
    {
        if (player == null || groundCollider == null || Time.timeScale <= 0f)
            return;

        float playerX = player.position.x;
        float targetX = playerX + spawnAhead;

        // A teleport should not spend many frames iterating over empty, passed slots.
        nextCoinX = SkipPassedSlots(nextCoinX, playerX - 2f, coinSpacing);
        nextFuelX = SkipPassedSlots(nextFuelX, playerX - 2f, fuelSpacing);

        // Avoid creating an unbounded number of objects in one frame.
        for (int i = 0; i < 25 && nextCoinX <= targetX; i++)
        {
            if (nextCoinX < playerX - 2f)
            {
                nextCoinX += coinSpacing;
                continue;
            }

            if (!TryFindGroundY(nextCoinX, out float surfaceY))
                break; // Terrain has not been generated here yet: retry next frame.

            // Only the chance of occupying this coin slot changes with distance.
            // Always advance after the decision so a skipped slot is not retried.
            if (ShouldSpawnCoin(nextCoinX))
            {
                GameObject coinPrefab = GetRandomCoinPrefab();
                if (coinPrefab != null)
                    Spawn(coinPrefab, nextCoinX, surfaceY + coinHeight);
            }

            nextCoinX += coinSpacing;
        }

        // Fuel generation is intentionally unchanged by adaptive coin difficulty.
        for (int i = 0; i < 10 && nextFuelX <= targetX; i++)
        {
            if (nextFuelX < playerX - 2f)
            {
                nextFuelX += fuelSpacing;
                continue;
            }

            if (!TryFindGroundY(nextFuelX, out float surfaceY))
                break;

            if (fuelPrefab != null)
                Spawn(fuelPrefab, nextFuelX, surfaceY + fuelHeight);

            nextFuelX += fuelSpacing;
        }

        // Dispose of pickups long after the car has passed them.
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            SpawnedPickup old = spawned[i];
            if (old.instance != null && old.x >= playerX - removeBehind) continue;
            if (old.instance != null) Destroy(old.instance);
            spawned.RemoveAt(i);
        }
    }

    internal static float SkipPassedSlots(float nextX, float minimumX, float spacing)
    {
        if (nextX >= minimumX) return nextX;
        return nextX + Mathf.Ceil((minimumX - nextX) / Mathf.Max(0.01f, spacing)) * Mathf.Max(0.01f, spacing);
    }

    private bool ShouldSpawnCoin(float slotX)
    {
        float chance = GetCoinSpawnChance(slotX);
        return chance >= 1f || Random.value < chance;
    }

    private float GetCoinSpawnChance(float slotX)
    {
        if (!enableAdaptiveCoinScarcity)
            return 1f;

        float distanceFromStart = Mathf.Max(0f, slotX - startingX);
        if (distanceFromStart < adaptiveStartDistance)
            return 1f;

        float safeStepDistance = Mathf.Max(1f, distancePerDifficultyStep);
        int steps = 1 + Mathf.FloorToInt(
            (distanceFromStart - adaptiveStartDistance) / safeStepDistance);

        return Mathf.Clamp(
            1f - steps * chanceReductionPerStep,
            minimumCoinSpawnChance,
            1f);
    }

    private GameObject GetRandomCoinPrefab()
    {
        return validCoinPrefabs == null || validCoinPrefabs.Length == 0 ? null :
            validCoinPrefabs[Random.Range(0, validCoinPrefabs.Length)];
    }

    private bool TryFindGroundY(float worldX, out float groundY)
    {
        groundY = 0f;

        Bounds bounds = groundCollider.bounds;
        if (!groundCollider.enabled || !groundCollider.gameObject.activeInHierarchy ||
            worldX < bounds.min.x || worldX > bounds.max.x)
            return false;

        // Raycast from above the ENTIRE generated collider.
        Vector2 start = new Vector2(worldX, bounds.max.y + 20f);
        float distance = Mathf.Max(100f, bounds.size.y + 50f);
        groundFilter.SetLayerMask(1 << groundCollider.gameObject.layer);
        groundHits.Clear();
        Physics2D.Raycast(start, Vector2.down, groundFilter, groundHits, distance);

        // Other colliders may share Ground's layer; select Ground's exact collider.
        foreach (RaycastHit2D hit in groundHits)
        {
            if (hit.collider == groundCollider)
            {
                groundY = hit.point.y;
                return true;
            }
        }

        return false;
    }

    private void Spawn(GameObject prefab, float x, float y)
    {
        GameObject instance = Instantiate(prefab, new Vector3(x, y, 0f), Quaternion.identity);
        spawned.Add(new SpawnedPickup(x, instance));
    }
}
