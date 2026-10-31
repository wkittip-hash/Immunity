using System;
using System.Collections.Generic;
using UnityEngine;

// Generic tile-to-tile movement for any immune cell (macrophage, neutrophil, T cell...).
// It only knows HOW to walk a path. Deciding WHERE to go is the job of a brain script like Macrophage.
public class CellMovement : MonoBehaviour
{
    [SerializeField] float speed = 2f;
    [Tooltip("Sprite that gets mirrored to face the walking direction. Found automatically if left empty.")]
    [SerializeField] SpriteRenderer spriteRenderer;
    [Tooltip("Tick if the artwork faces right. It will be mirrored when walking left (and vice versa).")]
    [SerializeField] bool spriteFacesRight = true;

    List<Vector3Int> path;
    int pathIndex;

    public bool IsMoving => path != null && pathIndex < path.Count;
    public Vector3Int CurrentCell => TissueGrid.Instance.WorldToCell(transform.position);
    public Vector3Int? Destination => IsMoving ? path[path.Count - 1] : (Vector3Int?)null;

    public event Action Arrived;

    void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = FindOnVisual<SpriteRenderer>(this);
    }

    // The sprite and Animator belong on a child object (e.g. "Visual") so animations can't pin the walking object.
    // Prefers a child's component over one left on the root (simple cells without a Visual child still work).
    public static T FindOnVisual<T>(Component owner) where T : Component
    {
        T fallback = null;
        foreach (var c in owner.GetComponentsInChildren<T>())
        {
            if (c.transform != owner.transform) return c;
            fallback = c;
        }
        return fallback;
    }

    void Start()
    {
        if (TissueGrid.Instance == null)
        {
            Debug.LogError("CellMovement: there is no TissueGrid in the scene. Add TissueGrid to your Grid object.", this);
            enabled = false;
            return;
        }

        // Snap onto the centre of the tile we were placed on.
        Vector3 snapped = TissueGrid.Instance.CellToWorld(CurrentCell);
        snapped.z = transform.position.z;
        transform.position = snapped;

        if (!TissueGrid.Instance.IsWalkable(CurrentCell))
            Debug.LogWarning(name + " is standing on a tile with no floor tile (or on an obstacle), so it may not be able to move.", this);
    }

    // Returns false if the target can't be reached.
    public bool MoveTo(Vector3Int targetCell)
    {
        var newPath = TissueGrid.Instance.FindPath(CurrentCell, targetCell);
        if (newPath == null) return false;

        path = newPath;
        pathIndex = 0;
        return true;
    }

    // Walk to any tile touching the target cell. Returns false if no such tile can be reached.
    public bool MoveNextTo(Vector3Int targetCell)
    {
        var newPath = TissueGrid.Instance.FindPathNextTo(CurrentCell, targetCell);
        if (newPath == null) return false;

        path = newPath;
        pathIndex = 0;
        return true;
    }

    public void Stop()
    {
        path = null;
    }

    // Mirror the sprite so it looks at a world position (e.g. the pathogen being attacked).
    public void FaceTowards(Vector3 worldPos)
    {
        if (spriteRenderer == null || Mathf.Abs(worldPos.x - transform.position.x) <= 0.01f) return;
        bool lookingLeft = worldPos.x < transform.position.x;
        spriteRenderer.flipX = spriteFacesRight ? lookingLeft : !lookingLeft;
    }

    void Update()
    {
        if (!IsMoving) return;

        Vector3 target = TissueGrid.Instance.CellToWorld(path[pathIndex]);
        target.z = transform.position.z;

        FaceTowards(target);

        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

        if ((transform.position - target).sqrMagnitude < 0.0001f)
        {
            pathIndex++;
            if (!IsMoving)
            {
                path = null;
                Arrived?.Invoke();
            }
        }
    }

    // Animations (e.g. the attack lunge) are keyed for art facing its original way and move the sprite's child
    // object, not this one. When the sprite is mirrored, mirror that motion too so the lunge goes toward the target.
    // Runs after the Animator, which rewrites the pose every frame, so this never accumulates.
    void LateUpdate()
    {
        if (spriteRenderer == null || !spriteRenderer.flipX) return;
        Transform visual = spriteRenderer.transform;
        if (visual == transform) return; // never move the walking object itself

        Vector3 p = visual.localPosition;
        visual.localPosition = new Vector3(-p.x, p.y, p.z);
        Vector3 e = visual.localEulerAngles;
        visual.localRotation = Quaternion.Euler(e.x, e.y, -e.z);
    }

    void OnDrawGizmosSelected()
    {
        if (!IsMoving || TissueGrid.Instance == null) return;
        Gizmos.color = Color.cyan;
        Vector3 from = transform.position;
        for (int i = pathIndex; i < path.Count; i++)
        {
            Vector3 to = TissueGrid.Instance.CellToWorld(path[i]);
            Gizmos.DrawLine(from, to);
            from = to;
        }
    }
}
