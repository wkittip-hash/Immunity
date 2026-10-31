using System;
using UnityEngine;

// Health for any cell or microbe. Destroys the object at 0 health.
// The sprite darkens as health drops so players can see who is losing the fight.
public class CellHealth : MonoBehaviour
{
    [SerializeField] float maxHealth = 100f;
    [Tooltip("Optional: tinted toward Damaged Color as health drops. Found automatically if left empty.")]
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] Color damagedColor = new Color(0.35f, 0.1f, 0.1f);

    public float Current { get; private set; }
    public float Max => maxHealth;
    public bool IsDead => Current <= 0f;

    public event Action<CellHealth> Died;

    Color baseColor = Color.white;

    void Awake()
    {
        Current = maxHealth;
        if (spriteRenderer == null) spriteRenderer = CellMovement.FindOnVisual<SpriteRenderer>(this);
        if (spriteRenderer != null) baseColor = spriteRenderer.color;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Max(0f, Current - amount);
        if (spriteRenderer != null)
            spriteRenderer.color = Color.Lerp(damagedColor, baseColor, Current / maxHealth);

        if (IsDead)
        {
            Debug.Log(name + " died.", this);
            Died?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
