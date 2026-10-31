using UnityEngine;
using UnityEngine.InputSystem;

// Colony-sim camera: WASD / arrow keys to pan, middle-mouse drag to pan, scroll wheel to zoom toward the cursor.
// Middle mouse is used for dragging because left/right click are taken by MacrophageCommander.
// Put this on the Main Camera (must be Orthographic).
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Keyboard pan")]
    [SerializeField] float panSpeed = 10f;
    [Tooltip("Hold Shift to pan faster.")]
    [SerializeField] float fastMultiplier = 2.5f;

    [Header("Mouse")]
    [SerializeField] bool dragWithMiddleMouse = true;
    [SerializeField] bool edgePan = false;
    [SerializeField] float edgePanBorder = 15f;

    [Header("Zoom")]
    [SerializeField] float zoomStep = 1f;
    [SerializeField] float minZoom = 2f;
    [SerializeField] float maxZoom = 20f;
    [SerializeField] float zoomSmoothing = 10f;

    [Header("Bounds (optional)")]
    [SerializeField] bool clampToBounds = false;
    [SerializeField] Vector2 minBounds = new Vector2(-50f, -50f);
    [SerializeField] Vector2 maxBounds = new Vector2(50f, 50f);

    Camera cam;
    float targetZoom;
    Vector3 dragOriginWorld;
    bool dragging;

    void Awake()
    {
        cam = GetComponent<Camera>();
        targetZoom = cam.orthographicSize;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard != null) KeyboardPan(keyboard, mouse);
        if (mouse != null)
        {
            MouseDrag(mouse);
            Zoom(mouse);
        }

        if (clampToBounds) ClampPosition();
    }

    void KeyboardPan(Keyboard keyboard, Mouse mouse)
    {
        Vector2 dir = Vector2.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) dir.y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) dir.y -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) dir.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) dir.x -= 1f;

        if (edgePan && mouse != null && !dragging)
        {
            Vector2 m = mouse.position.ReadValue();
            if (m.x <= edgePanBorder) dir.x -= 1f;
            if (m.x >= Screen.width - edgePanBorder) dir.x += 1f;
            if (m.y <= edgePanBorder) dir.y -= 1f;
            if (m.y >= Screen.height - edgePanBorder) dir.y += 1f;
        }

        if (dir == Vector2.zero) return;

        float speed = panSpeed;
        if (keyboard.leftShiftKey.isPressed) speed *= fastMultiplier;
        // Scale by zoom so panning feels the same zoomed in or out.
        speed *= cam.orthographicSize / 5f;

        transform.position += (Vector3)(dir.normalized * speed * Time.unscaledDeltaTime);
    }

    void MouseDrag(Mouse mouse)
    {
        if (!dragWithMiddleMouse) return;

        if (mouse.middleButton.wasPressedThisFrame)
        {
            dragOriginWorld = MouseWorld(mouse);
            dragging = true;
        }
        else if (mouse.middleButton.wasReleasedThisFrame)
        {
            dragging = false;
        }

        if (dragging)
        {
            // Keep the point we grabbed under the cursor.
            transform.position += dragOriginWorld - MouseWorld(mouse);
        }
    }

    void Zoom(Mouse mouse)
    {
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            targetZoom = Mathf.Clamp(targetZoom - Mathf.Sign(scroll) * zoomStep, minZoom, maxZoom);

        if (Mathf.Approximately(cam.orthographicSize, targetZoom)) return;

        // Zoom toward the cursor: the world point under the mouse stays put.
        Vector3 before = MouseWorld(mouse);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, zoomSmoothing * Time.unscaledDeltaTime);
        if (Mathf.Abs(cam.orthographicSize - targetZoom) < 0.01f) cam.orthographicSize = targetZoom;
        Vector3 after = MouseWorld(mouse);

        transform.position += before - after;
        if (dragging) dragOriginWorld = MouseWorld(mouse);
    }

    Vector3 MouseWorld(Mouse mouse)
    {
        Vector3 world = cam.ScreenToWorldPoint(mouse.position.ReadValue());
        world.z = 0f;
        return world;
    }

    void ClampPosition()
    {
        Vector3 p = transform.position;
        p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
        p.y = Mathf.Clamp(p.y, minBounds.y, maxBounds.y);
        transform.position = p;
    }
}
