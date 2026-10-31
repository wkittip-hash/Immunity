using System.Collections.Generic;
using UnityEngine;

// Makes an object clickable and draws an outline around its sprite while it's selected.
// The outline is 8 flat-colour copies of the sprite's shape, nudged in 8 directions and drawn behind it,
// so only a thin rim shows around the character's edge. Works with any sprite, no extra art needed.
// Macrophage and Pathogen add this automatically.
public class Selectable : MonoBehaviour
{
    public static readonly List<Selectable> All = new List<Selectable>();

    static readonly Vector2[] Directions =
    {
        new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
        new Vector2(0.7071f, 0.7071f), new Vector2(-0.7071f, 0.7071f),
        new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, -0.7071f)
    };

    [SerializeField] Color outlineColor = new Color(1f, 0.9f, 0.2f, 1f);
    [Tooltip("Outline thickness in world units (one tile is usually 1).")]
    [SerializeField] float outlineWidth = 0.03f;
    [Tooltip("0 = steady outline. Higher = faster gentle pulse.")]
    [SerializeField] float pulseSpeed = 0f;
    [Tooltip("Optional: your own indicator (e.g. a ring sprite child). If set, it's shown instead of the outline.")]
    [SerializeField] GameObject customIndicator;
    [Tooltip("While selected, the character's Order in Layer is raised by this much so it and its outline " +
             "draw above tiles and other characters.")]
    [SerializeField] int selectedSortingBoost = 100;

    public bool IsSelected { get; private set; }

    static Material silhouetteMaterial;
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

    MaterialPropertyBlock colorBlock;

    SpriteRenderer source;
    int originalSortingOrder;
    readonly List<SpriteRenderer> outlineParts = new List<SpriteRenderer>();

    void Awake()
    {
        source = CellMovement.FindOnVisual<SpriteRenderer>(this);
        if (source != null) originalSortingOrder = source.sortingOrder;
        if (customIndicator == null && source != null) BuildOutline();
        SetSelected(false);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void BuildOutline()
    {
        if (silhouetteMaterial == null)
        {
            var shader = Resources.Load<Shader>("SpriteSilhouette");
            if (shader == null)
            {
                Debug.LogError("Selectable: can't find Assets/Resources/SpriteSilhouette.shader, outline disabled.", this);
                return;
            }
            silhouetteMaterial = new Material(shader);
        }

        var root = new GameObject("SelectionOutline").transform;
        root.SetParent(source.transform, false);

        foreach (var dir in Directions)
        {
            var part = new GameObject("Part").AddComponent<SpriteRenderer>();
            part.transform.SetParent(root, false);
            part.sharedMaterial = silhouetteMaterial;
            part.sortingLayerID = source.sortingLayerID;
            part.sortingOrder = source.sortingOrder - 1; // behind the character, so only the rim shows
            outlineParts.Add(part);
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (source != null)
            source.sortingOrder = selected ? originalSortingOrder + selectedSortingBoost : originalSortingOrder;
        if (customIndicator != null) customIndicator.SetActive(selected);
        foreach (var part in outlineParts) part.enabled = selected;
        if (selected) UpdateOutline();
    }

    void LateUpdate()
    {
        if (IsSelected) UpdateOutline();
    }

    void UpdateOutline()
    {
        if (outlineParts.Count == 0) return;

        Color color = outlineColor;
        if (pulseSpeed > 0f)
            color.a *= Mathf.Lerp(0.5f, 1f, (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f);

        // Convert the world-space width into the sprite's local space, so scaled sprites get the same thickness.
        colorBlock ??= new MaterialPropertyBlock();
        colorBlock.SetColor(OutlineColorId, color);

        Vector3 scale = source.transform.lossyScale;
        float sx = Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
        float sy = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);

        for (int i = 0; i < outlineParts.Count; i++)
        {
            var part = outlineParts[i];
            // Follow sprite changes (animation, facing direction).
            part.sprite = source.sprite;
            // URP applies SpriteRenderer.flipX inside the sprite shader, which our silhouette shader doesn't do,
            // so mirror the copy with a negative scale instead.
            part.transform.localScale = new Vector3(source.flipX ? -1f : 1f, source.flipY ? -1f : 1f, 1f);
            part.SetPropertyBlock(colorBlock);
            part.sortingLayerID = source.sortingLayerID;
            part.sortingOrder = source.sortingOrder - 1; // just behind the (boosted) character
            part.transform.localPosition = new Vector3(Directions[i].x * outlineWidth / sx,
                                                       Directions[i].y * outlineWidth / sy, 0f);
        }
    }
}
