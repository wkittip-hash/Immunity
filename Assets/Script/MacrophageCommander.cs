using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Player input:
//  - Left-click a macrophage or pathogen to select (highlight) it.
//  - Left-drag to draw a select box. If the box has macrophages in it, only they are selected.
//  - Hold Shift while clicking/dragging to add to the current selection.
//  - With macrophages selected, right-click a tile to send them there (they spread out around it),
//    or right-click a pathogen to order them all to attack it.
// Put this on any object in the scene (e.g. the Main Camera).
public class MacrophageCommander : MonoBehaviour
{
    [SerializeField] Camera cam;
    [Tooltip("How close (world units) a click must be to an object to select it, when it has no collider.")]
    [SerializeField] float clickRadius = 0.6f;
    [Tooltip("Pixels the mouse must move before a click turns into a select box.")]
    [SerializeField] float dragThreshold = 6f;

    [Header("Select box look")]
    [SerializeField] Color boxFillColor = new Color(0.3f, 0.8f, 1f, 0.15f);
    [SerializeField] Color boxBorderColor = new Color(0.3f, 0.8f, 1f, 0.9f);
    [SerializeField] float boxBorderWidth = 2f;

    readonly List<Selectable> selection = new List<Selectable>();

    bool pressing;
    bool boxActive;
    Vector2 pressScreenPos;
    Vector2 currentScreenPos;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        selection.RemoveAll(s => s == null); // forget cells that died

        var mouse = Mouse.current;
        if (mouse == null) return;

        // The build tool owns the mouse while it's active.
        if (BuildingPlacer.IsPlacing)
        {
            pressing = false;
            boxActive = false;
            return;
        }

        currentScreenPos = mouse.position.ReadValue();
        bool additive = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        // Clicks on UI buttons/panels shouldn't select or deselect cells behind them.
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (mouse.leftButton.wasPressedThisFrame && !overUI)
        {
            pressing = true;
            boxActive = false;
            pressScreenPos = currentScreenPos;
        }

        if (pressing && (currentScreenPos - pressScreenPos).magnitude > dragThreshold)
            boxActive = true;

        if (pressing && mouse.leftButton.wasReleasedThisFrame)
        {
            if (boxActive) BoxSelect(pressScreenPos, currentScreenPos, additive);
            else ClickSelect(ScreenToWorld(currentScreenPos), additive);
            pressing = false;
            boxActive = false;
        }

        if (mouse.rightButton.wasPressedThisFrame && !overUI)
            GiveOrder(ScreenToWorld(currentScreenPos));
    }

    Vector3 ScreenToWorld(Vector2 screen)
    {
        Vector3 world = cam.ScreenToWorldPoint(screen);
        world.z = 0f;
        return world;
    }

    // ---------- Selecting ----------

    void ClickSelect(Vector3 world, bool additive)
    {
        var clicked = FindSelectableAt(world);
        if (!additive) ClearSelection();
        if (clicked == null) return;

        if (additive && selection.Contains(clicked)) RemoveFromSelection(clicked); // shift-click toggles
        else AddToSelection(clicked);
        LogSelection();
    }

    void BoxSelect(Vector2 screenA, Vector2 screenB, bool additive)
    {
        Vector3 a = ScreenToWorld(screenA), b = ScreenToWorld(screenB);
        var worldRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                        Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        var inside = new List<Selectable>();
        bool anyMacrophage = false;
        foreach (var s in Selectable.All)
        {
            if (!worldRect.Contains(s.transform.position)) continue;
            inside.Add(s);
            if (s.TryGetComponent(out Macrophage _)) anyMacrophage = true;
        }

        if (!additive) ClearSelection();
        foreach (var s in inside)
        {
            // Like an RTS: boxing your units ignores the enemies mixed in with them.
            if (anyMacrophage && !s.TryGetComponent(out Macrophage _)) continue;
            AddToSelection(s);
        }
        LogSelection();
    }

    // Uses a Collider2D if the object has one, otherwise picks the nearest selectable within clickRadius.
    Selectable FindSelectableAt(Vector3 world)
    {
        var hit = Physics2D.OverlapPoint(world);
        var clicked = hit != null ? hit.GetComponentInParent<Selectable>() : null;
        if (clicked != null) return clicked;

        Selectable best = null;
        float bestDist = clickRadius * clickRadius;
        foreach (var s in Selectable.All)
        {
            Vector3 offset = s.transform.position - world;
            offset.z = 0f;
            if (offset.sqrMagnitude <= bestDist)
            {
                bestDist = offset.sqrMagnitude;
                best = s;
            }
        }
        return best;
    }

    void AddToSelection(Selectable s)
    {
        if (selection.Contains(s)) return;
        selection.Add(s);
        s.SetSelected(true);
    }

    void RemoveFromSelection(Selectable s)
    {
        selection.Remove(s);
        s.SetSelected(false);
    }

    void ClearSelection()
    {
        foreach (var s in selection) s.SetSelected(false);
        selection.Clear();
    }

    void LogSelection()
    {
        if (selection.Count == 0) return;
        if (selection.Count > 1)
        {
            Debug.Log("Selected " + selection.Count + " cells");
            return;
        }

        var selected = selection[0];
        string info = "Selected " + selected.name;
        if (selected.TryGetComponent(out Pathogen pathogen)) info += " (" + pathogen.displayName + ")";
        if (selected.TryGetComponent(out CellHealth health)) info += "  Health " + Mathf.CeilToInt(health.Current) + "/" + health.Max;
        Debug.Log(info);
    }

    // ---------- Orders ----------

    void GiveOrder(Vector3 world)
    {
        var macrophages = new List<Macrophage>();
        foreach (var s in selection)
            if (s.TryGetComponent(out Macrophage m)) macrophages.Add(m);
        if (macrophages.Count == 0) return;

        var clicked = FindSelectableAt(world);
        if (clicked != null && clicked.TryGetComponent(out Pathogen pathogen))
        {
            foreach (var m in macrophages) m.CommandAttack(pathogen);
            return;
        }

        // Give each macrophage its own tile around the click so they don't stack on one spot.
        Vector3Int clickedCell = TissueGrid.Instance.WorldToCell(world);
        var cells = FindSpreadCells(clickedCell, macrophages.Count);
        if (cells.Count == 0)
        {
            Debug.Log("Macrophages can't reach that tile.");
            return;
        }
        for (int i = 0; i < macrophages.Count; i++)
            macrophages[i].CommandMoveTo(cells[i % cells.Count]);
    }

    // Walkable tiles closest to the centre, found by spreading outward (breadth-first) through open tiles.
    static List<Vector3Int> FindSpreadCells(Vector3Int center, int count)
    {
        var grid = TissueGrid.Instance;
        var result = new List<Vector3Int>();
        if (!grid.IsWalkable(center)) return result;

        var queue = new Queue<Vector3Int>();
        var visited = new HashSet<Vector3Int> { center };
        queue.Enqueue(center);

        while (queue.Count > 0 && result.Count < count)
        {
            var cell = queue.Dequeue();
            result.Add(cell);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var next = cell + new Vector3Int(dx, dy, 0);
                if (visited.Add(next) && grid.IsWalkable(next)) queue.Enqueue(next);
            }
        }
        return result;
    }

    // ---------- Drawing the box ----------

    void OnGUI()
    {
        if (!boxActive) return;

        // GUI space has y pointing down; mouse space has y pointing up.
        Vector2 a = new Vector2(pressScreenPos.x, Screen.height - pressScreenPos.y);
        Vector2 b = new Vector2(currentScreenPos.x, Screen.height - currentScreenPos.y);
        var rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        DrawRect(rect, boxFillColor);
        float w = boxBorderWidth;
        DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, w), boxBorderColor);
        DrawRect(new Rect(rect.xMin, rect.yMax - w, rect.width, w), boxBorderColor);
        DrawRect(new Rect(rect.xMin, rect.yMin, w, rect.height), boxBorderColor);
        DrawRect(new Rect(rect.xMax - w, rect.yMin, w, rect.height), boxBorderColor);
    }

    static void DrawRect(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }
}
