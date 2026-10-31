using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Building placement tool (like Oxygen Not Included's build menu).
// Hook a UI Button's OnClick to BuildingPlacer.BeginPlacement and drag the building prefab into the slot.
//  - A see-through preview follows the mouse, snapped to the grid: green = can build, red = can't.
//  - Left-click to build. Hold Shift to keep building more.
//  - Right-click or Escape to cancel.
// Put this on any object in the scene (e.g. the Main Camera).
public class BuildingPlacer : MonoBehaviour
{
    public static BuildingPlacer Instance { get; private set; }

    // True while placing, and for the frame placement ended, so that click doesn't also select or order units.
    public static bool IsPlacing => Instance != null &&
                                    (Instance.prefab != null || Time.frameCount == Instance.lastActiveFrame);

    [SerializeField] Camera cam;
    [SerializeField] Color validColor = new Color(0.4f, 1f, 0.4f, 0.6f);
    [SerializeField] Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.6f);

    CapillaryPort prefab;
    SpriteRenderer ghost;
    int lastActiveFrame = -1;

    void Awake()
    {
        Instance = this;
        if (cam == null) cam = Camera.main;
    }

    // Called by the UI button.
    public void BeginPlacement(CapillaryPort buildingPrefab)
    {
        CancelPlacement();
        if (buildingPrefab == null) return;
        prefab = buildingPrefab;

        var prefabSprite = prefab.GetComponentInChildren<SpriteRenderer>();
        ghost = new GameObject("PlacementPreview").AddComponent<SpriteRenderer>();
        if (prefabSprite != null)
        {
            ghost.sprite = prefabSprite.sprite;
            ghost.sortingLayerID = prefabSprite.sortingLayerID;
        }
        ghost.sortingOrder = 1000; // always visible above tiles and cells
        ghost.transform.localScale = CapillaryPort.FitScale(ghost.sprite, prefab.Size);
    }

    public void CancelPlacement()
    {
        if (prefab != null) lastActiveFrame = Time.frameCount;
        prefab = null;
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;
    }

    void Update()
    {
        if (prefab == null) return;
        lastActiveFrame = Time.frameCount;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null || TissueGrid.Instance == null) return;

        if (mouse.rightButton.wasPressedThisFrame || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
        {
            CancelPlacement();
            return;
        }

        Vector3 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        world.z = 0f;
        Vector3Int origin = CapillaryPort.OriginForWorldCenter(world, prefab.Size);
        bool valid = CanPlace(origin, prefab.Size);

        ghost.transform.position = CapillaryPort.FootprintCenter(origin, prefab.Size);
        ghost.color = valid ? validColor : invalidColor;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (mouse.leftButton.wasPressedThisFrame && !overUI)
        {
            if (!valid)
            {
                Debug.Log("Can't build there: every tile must be open floor with no walls, buildings or cells on it.");
                return;
            }

            Vector3 pos = ghost.transform.position;
            pos.z = prefab.transform.position.z;
            var building = Instantiate(prefab, pos, Quaternion.identity);
            building.Place(origin);

            bool keepBuilding = keyboard != null && keyboard.shiftKey.isPressed;
            if (!keepBuilding) CancelPlacement();
        }
    }

    // Every footprint tile must be open floor (no wall, no other building) with no cell standing on it.
    static bool CanPlace(Vector3Int origin, Vector2Int size)
    {
        var grid = TissueGrid.Instance;
        foreach (var cell in CapillaryPort.FootprintCells(origin, size))
        {
            if (!grid.IsWalkable(cell)) return false;
            if (IsOccupied(cell)) return false;
        }
        return true;
    }

    static bool IsOccupied(Vector3Int cell)
    {
        var grid = TissueGrid.Instance;
        foreach (var m in Macrophage.All)
            if (grid.WorldToCell(m.transform.position) == cell) return true;
        foreach (var p in Pathogen.All)
            if (grid.WorldToCell(p.transform.position) == cell) return true;
        return false;
    }
}
