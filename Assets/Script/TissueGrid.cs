using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// The walkable tissue map. Every cell that has a floor tile and no obstacle tile can be walked on.
// Put ONE of these in the scene (e.g. on the Grid object) and assign the tilemaps.
public class TissueGrid : MonoBehaviour
{
    public static TissueGrid Instance { get; private set; }

    [Tooltip("Tilemap that marks where cells can move (tissue / blood vessel floor).")]
    [SerializeField] Tilemap floorTilemap;
    [Tooltip("Tilemaps that block movement (cell walls, bone, membranes...).")]
    [SerializeField] Tilemap[] obstacleTilemaps;
    [SerializeField] bool allowDiagonal = true;
    [Tooltip("Safety limit so a path search on a huge map can't freeze the game.")]
    [SerializeField] int maxSearchNodes = 5000;

    static readonly Vector3Int[] OrthogonalDirs =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };
    static readonly Vector3Int[] DiagonalDirs =
    {
        new Vector3Int(1, 1, 0), new Vector3Int(1, -1, 0), new Vector3Int(-1, 1, 0), new Vector3Int(-1, -1, 0)
    };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("More than one TissueGrid in the scene, ignoring " + name);
            return;
        }
        Instance = this;

        // Fall back to the first Tilemap under this object (e.g. Grid > Tilemap) if none was assigned.
        if (floorTilemap == null)
        {
            floorTilemap = GetComponentInChildren<Tilemap>();
            if (floorTilemap != null)
                Debug.Log("TissueGrid: Floor Tilemap not assigned, using '" + floorTilemap.name + "'.", this);
            else
                Debug.LogError("TissueGrid: no Floor Tilemap assigned and none found in children. " +
                               "Put TissueGrid on the Grid object or drag your floor Tilemap into the slot.", this);
        }
        obstacleTilemaps ??= new Tilemap[0];
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Cells covered by buildings (e.g. a capillary port). Nothing can walk through them.
    readonly HashSet<Vector3Int> blockedCells = new HashSet<Vector3Int>();

    public Vector3Int WorldToCell(Vector3 worldPos) => floorTilemap.WorldToCell(worldPos);
    public Vector3 CellToWorld(Vector3Int cell) => floorTilemap.GetCellCenterWorld(cell);
    public Vector3 CellSize => floorTilemap.cellSize;

    public void SetBlocked(Vector3Int cell, bool blocked)
    {
        if (blocked) blockedCells.Add(cell);
        else blockedCells.Remove(cell);
    }

    public bool IsWalkable(Vector3Int cell)
    {
        if (blockedCells.Contains(cell)) return false;
        if (!floorTilemap.HasTile(cell)) return false;
        foreach (var obstacles in obstacleTilemaps)
        {
            if (obstacles != null && obstacles.HasTile(cell)) return false;
        }
        return true;
    }

    // Tiles between two cells, counting diagonals as 1 (so 1 = touching).
    public static int CellDistance(Vector3Int a, Vector3Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    // A* search. Returns the list of cells to walk through (excluding start), or null if unreachable.
    public List<Vector3Int> FindPath(Vector3Int start, Vector3Int goal)
    {
        if (!IsWalkable(goal)) return null;
        return Search(start, goal, cell => cell == goal);
    }

    // Path to any walkable cell touching the target. Used for chasing: we only need to be next to it,
    // and it still works if the target stands on a blocked tile.
    public List<Vector3Int> FindPathNextTo(Vector3Int start, Vector3Int target)
    {
        return Search(start, target, cell => CellDistance(cell, target) <= 1);
    }

    List<Vector3Int> Search(Vector3Int start, Vector3Int goal, System.Func<Vector3Int, bool> isGoal)
    {
        if (isGoal(start)) return new List<Vector3Int>();

        var open = new MinHeap();
        var cameFrom = new Dictionary<Vector3Int, Vector3Int>();
        var costSoFar = new Dictionary<Vector3Int, int> { [start] = 0 };
        open.Push(start, Heuristic(start, goal));

        int expanded = 0;
        while (open.Count > 0 && expanded < maxSearchNodes)
        {
            Vector3Int current = open.Pop();
            if (isGoal(current)) return BuildPath(cameFrom, start, current);
            expanded++;

            int currentCost = costSoFar[current];
            foreach (var (next, stepCost) in Neighbours(current))
            {
                int newCost = currentCost + stepCost;
                if (costSoFar.TryGetValue(next, out int oldCost) && newCost >= oldCost) continue;

                costSoFar[next] = newCost;
                cameFrom[next] = current;
                open.Push(next, newCost + Heuristic(next, goal));
            }
        }
        return null;
    }

    IEnumerable<(Vector3Int cell, int cost)> Neighbours(Vector3Int cell)
    {
        foreach (var dir in OrthogonalDirs)
        {
            var next = cell + dir;
            if (IsWalkable(next)) yield return (next, 10);
        }
        if (!allowDiagonal) yield break;

        foreach (var dir in DiagonalDirs)
        {
            var next = cell + dir;
            // No corner cutting: both side cells must be open, so cells can't squeeze through walls.
            if (IsWalkable(next) &&
                IsWalkable(cell + new Vector3Int(dir.x, 0, 0)) &&
                IsWalkable(cell + new Vector3Int(0, dir.y, 0)))
            {
                yield return (next, 14);
            }
        }
    }

    int Heuristic(Vector3Int a, Vector3Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        if (!allowDiagonal) return 10 * (dx + dy);
        return 10 * (dx + dy) - 6 * Mathf.Min(dx, dy); // octile distance
    }

    static List<Vector3Int> BuildPath(Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int start, Vector3Int goal)
    {
        var path = new List<Vector3Int>();
        var cell = goal;
        while (cell != start)
        {
            path.Add(cell);
            cell = cameFrom[cell];
        }
        path.Reverse();
        return path;
    }

    // Small binary heap so A* stays fast on bigger maps (Unity has no built-in PriorityQueue).
    class MinHeap
    {
        readonly List<(Vector3Int cell, int priority)> items = new List<(Vector3Int, int)>();
        public int Count => items.Count;

        public void Push(Vector3Int cell, int priority)
        {
            items.Add((cell, priority));
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (items[parent].priority <= items[i].priority) break;
                (items[parent], items[i]) = (items[i], items[parent]);
                i = parent;
            }
        }

        public Vector3Int Pop()
        {
            var top = items[0].cell;
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < items.Count && items[left].priority < items[smallest].priority) smallest = left;
                if (right < items.Count && items[right].priority < items[smallest].priority) smallest = right;
                if (smallest == i) break;
                (items[smallest], items[i]) = (items[i], items[smallest]);
                i = smallest;
            }
            return top;
        }
    }
}
