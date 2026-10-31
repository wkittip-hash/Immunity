using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The "swarm nest" for macrophages: a capillary (pink tube) in the tissue.
// Biology: monocytes travel in the blood, squeeze out through the vessel wall (extravasation),
// then differentiate into macrophages in the tissue. Inflammation nearby (pathogens) speeds this up.
//
// Setup: make a prefab with a SpriteRenderer (PinkTube) + this script, and assign your macrophage prefab.
// Place it with BuildingPlacer, or drop it straight into the scene.
public class CapillaryPort : MonoBehaviour
{
    [Header("Footprint")]
    [SerializeField] Vector2Int size = new Vector2Int(2, 2);
    [Tooltip("Scale the sprite so it exactly covers the footprint, whatever its Pixels Per Unit.")]
    [SerializeField] bool fitSpriteToFootprint = true;

    [Header("Spawning")]
    [Tooltip("Prefab with Macrophage + CellMovement on it.")]
    [SerializeField] Macrophage cellPrefab;
    [SerializeField] float spawnInterval = 8f;
    [Tooltip("Stops spawning while this many of its cells are alive.")]
    [SerializeField] int maxAlive = 5;

    [Header("Inflammation (pathogens nearby -> faster recruitment)")]
    [SerializeField] float inflammationRadius = 8f;
    [SerializeField] float inflamedSpeedMultiplier = 2f;

    [Header("Monocyte -> macrophage")]
    [Tooltip("Seconds the new cell spends growing from a small monocyte into a macrophage.")]
    [SerializeField] float differentiationTime = 1.5f;
    [SerializeField] float monocyteScale = 0.4f;

    [Header("Look")]
    [Tooltip("The sprite pulses this colour just before a cell comes out.")]
    [SerializeField] Color spawnGlowColor = new Color(1f, 0.75f, 0.3f);
    [SerializeField] float glowWarningTime = 1.5f;

    public Vector2Int Size => size;
    public Vector3Int Origin { get; private set; } // bottom-left cell of the footprint

    readonly List<Macrophage> spawned = new List<Macrophage>();
    SpriteRenderer spriteRenderer;
    Color baseColor;
    float timer;
    bool placed;

    void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) baseColor = spriteRenderer.color;
        timer = spawnInterval;
    }

    void Start()
    {
        if (TissueGrid.Instance == null)
        {
            Debug.LogError("CapillaryPort: there is no TissueGrid in the scene.", this);
            enabled = false;
            return;
        }
        // Dropped into the scene by hand rather than placed with BuildingPlacer.
        if (!placed) Place(OriginForWorldCenter(transform.position, size));
        if (cellPrefab == null) Debug.LogWarning(name + ": no Cell Prefab assigned, nothing will spawn.", this);
    }

    // Snaps the building onto the grid and blocks its tiles.
    public void Place(Vector3Int origin)
    {
        var grid = TissueGrid.Instance;
        Origin = origin;
        placed = true;

        Vector3 center = FootprintCenter(origin, size);
        center.z = transform.position.z;
        transform.position = center;
        if (fitSpriteToFootprint && spriteRenderer != null)
            spriteRenderer.transform.localScale = FitScale(spriteRenderer.sprite, size);

        foreach (var cell in FootprintCells(origin, size)) grid.SetBlocked(cell, true);
    }

    void OnDestroy()
    {
        if (!placed || TissueGrid.Instance == null) return;
        foreach (var cell in FootprintCells(Origin, size)) TissueGrid.Instance.SetBlocked(cell, false);
    }

    void Update()
    {
        if (!placed || cellPrefab == null) return;

        spawned.RemoveAll(m => m == null);
        if (spawned.Count >= maxAlive)
        {
            SetGlow(0f);
            return;
        }

        timer -= Time.deltaTime * (IsInflamed() ? inflamedSpeedMultiplier : 1f);

        if (timer <= glowWarningTime)
            SetGlow((Mathf.Sin(Time.time * 10f) + 1f) * 0.5f);

        if (timer <= 0f)
        {
            timer = spawnInterval;
            SetGlow(0f);
            Spawn();
        }
    }

    bool IsInflamed()
    {
        Vector3 center = transform.position;
        float r = inflammationRadius * inflammationRadius;
        foreach (var p in Pathogen.All)
        {
            Vector3 offset = p.transform.position - center;
            offset.z = 0f;
            if (offset.sqrMagnitude <= r) return true;
        }
        return false;
    }

    void SetGlow(float amount)
    {
        if (spriteRenderer != null) spriteRenderer.color = Color.Lerp(baseColor, spawnGlowColor, amount);
    }

    void Spawn()
    {
        if (!TryGetExitCell(out Vector3Int exit))
        {
            Debug.Log(name + ": no free floor tile next to the capillary, can't release a cell.", this);
            return;
        }

        Vector3 pos = TissueGrid.Instance.CellToWorld(exit);
        pos.z = cellPrefab.transform.position.z;
        var cell = Instantiate(cellPrefab, pos, Quaternion.identity);
        spawned.Add(cell);
        cell.StartCoroutine(Differentiate(cell, differentiationTime, monocyteScale));
    }

    // A walkable tile touching the footprint. Prefers the bottom edge, where the spawn gap is in the art.
    bool TryGetExitCell(out Vector3Int exit)
    {
        var grid = TissueGrid.Instance;
        var bottom = new List<Vector3Int>();
        var other = new List<Vector3Int>();

        for (int x = Origin.x - 1; x <= Origin.x + size.x; x++)
        for (int y = Origin.y - 1; y <= Origin.y + size.y; y++)
        {
            bool inside = x >= Origin.x && x < Origin.x + size.x && y >= Origin.y && y < Origin.y + size.y;
            var cell = new Vector3Int(x, y, 0);
            if (inside || !grid.IsWalkable(cell)) continue;
            (y == Origin.y - 1 ? bottom : other).Add(cell);
        }

        var pick = bottom.Count > 0 ? bottom : other;
        exit = pick.Count > 0 ? pick[Random.Range(0, pick.Count)] : default;
        return pick.Count > 0;
    }

    // The new cell starts as a small monocyte and grows into a macrophage before it starts acting.
    static IEnumerator Differentiate(Macrophage cell, float duration, float startScale)
    {
        Vector3 fullScale = cell.transform.localScale;
        cell.enabled = false; // brain off: not hunting or targetable yet

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(startScale, 1f, t / duration);
            cell.transform.localScale = fullScale * k;
            yield return null;
        }

        cell.transform.localScale = fullScale;
        cell.enabled = true;
    }

    // ---------- Footprint helpers (also used by BuildingPlacer) ----------

    public static IEnumerable<Vector3Int> FootprintCells(Vector3Int origin, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
            yield return origin + new Vector3Int(x, y, 0);
    }

    public static Vector3 FootprintCenter(Vector3Int origin, Vector2Int size)
    {
        var grid = TissueGrid.Instance;
        return (grid.CellToWorld(origin) + grid.CellToWorld(origin + new Vector3Int(size.x - 1, size.y - 1, 0))) * 0.5f;
    }

    // Bottom-left cell for a footprint centred as close as possible to a world point (e.g. the mouse).
    public static Vector3Int OriginForWorldCenter(Vector3 world, Vector2Int size)
    {
        Vector3 cellSize = TissueGrid.Instance.CellSize;
        world.x -= (size.x - 1) * 0.5f * cellSize.x;
        world.y -= (size.y - 1) * 0.5f * cellSize.y;
        return TissueGrid.Instance.WorldToCell(world);
    }

    public static Vector3 FitScale(Sprite sprite, Vector2Int size)
    {
        if (sprite == null) return Vector3.one;
        Vector3 cellSize = TissueGrid.Instance.CellSize;
        Vector3 spriteSize = sprite.bounds.size;
        return new Vector3(size.x * cellSize.x / spriteSize.x, size.y * cellSize.y / spriteSize.y, 1f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, inflammationRadius);
    }
}
