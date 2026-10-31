using System.Collections.Generic;
using UnityEngine;

// Anything a macrophage can detect and eat (bacteria, virus particles, dead cell debris...).
// It hunts macrophages and fights back by releasing toxins that damage them
// (like the leukocidins of Staphylococcus aureus, which kill white blood cells).
public class Pathogen : MonoBehaviour
{
    public static readonly List<Pathogen> All = new List<Pathogen>();

    [Tooltip("Shown in UI / learning notes, e.g. 'Staphylococcus aureus'.")]
    public string displayName = "Bacterium";

    [Header("Hunting")]
    [Tooltip("World units. Macrophages closer than this get chased.")]
    [SerializeField] float senseRadius = 6f;
    [SerializeField] float repathInterval = 0.5f;
    [SerializeField] int wanderRadius = 3;
    [SerializeField] Vector2 idleTimeRange = new Vector2(1f, 3f);

    [Header("Toxin attack")]
    [Tooltip("Damage per second dealt to each macrophage in range.")]
    [SerializeField] float toxinDamagePerSecond = 10f;
    [Tooltip("Tiles. 1 = the 8 tiles around it.")]
    [SerializeField] int toxinRangeTiles = 1;

    // Set while a macrophage is hunting it, so two macrophages don't chase the same target.
    public Macrophage ClaimedBy { get; set; }
    public CellHealth Health { get; private set; }

    CellMovement movement;
    Macrophage prey;
    float timer;
    bool wasAttacking;

    void Awake()
    {
        Health = GetComponent<CellHealth>();
        if (Health == null) Health = gameObject.AddComponent<CellHealth>();
        movement = GetComponent<CellMovement>();
        if (movement == null) movement = gameObject.AddComponent<CellMovement>();
        if (GetComponent<Selectable>() == null) gameObject.AddComponent<Selectable>();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        // CellMovement turns itself off if the scene has no TissueGrid; it has already logged why.
        if (!movement.enabled) return;

        ReleaseToxins();
        Hunt();
    }

    void ReleaseToxins()
    {
        Vector3Int here = movement.CurrentCell;
        bool attacking = false;

        // Copy because a macrophage can die (and leave the list) during the loop.
        foreach (var m in Macrophage.All.ToArray())
        {
            Vector3Int cell = TissueGrid.Instance.WorldToCell(m.transform.position);
            if (TissueGrid.CellDistance(here, cell) > toxinRangeTiles) continue;

            if (!wasAttacking) Debug.Log(name + " releases toxins on " + m.name + ".", this);
            attacking = true;
            m.Health.TakeDamage(toxinDamagePerSecond * Time.deltaTime);
        }
        wasAttacking = attacking;
    }

    void Hunt()
    {
        if (prey == null || !InSenseRange(prey)) prey = FindNearestMacrophage();

        if (prey == null)
        {
            Wander();
            return;
        }

        Vector3Int preyCell = TissueGrid.Instance.WorldToCell(prey.transform.position);
        if (TissueGrid.CellDistance(movement.CurrentCell, preyCell) <= 1)
        {
            movement.Stop(); // close enough, keep attacking
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = repathInterval;
        movement.MoveNextTo(preyCell);
    }

    void Wander()
    {
        if (movement.IsMoving) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = Random.Range(idleTimeRange.x, idleTimeRange.y);

        var offset = new Vector3Int(Random.Range(-wanderRadius, wanderRadius + 1),
                                    Random.Range(-wanderRadius, wanderRadius + 1), 0);
        movement.MoveTo(movement.CurrentCell + offset); // ignored if that tile isn't walkable
    }

    Macrophage FindNearestMacrophage()
    {
        Macrophage best = null;
        float bestDist = float.MaxValue;
        foreach (var m in Macrophage.All)
        {
            float d = FlatDistanceSqr(m.transform.position);
            if (d <= senseRadius * senseRadius && d < bestDist)
            {
                bestDist = d;
                best = m;
            }
        }
        if (best != null) timer = 0f; // re-path right away
        return best;
    }

    bool InSenseRange(Macrophage m) => FlatDistanceSqr(m.transform.position) <= senseRadius * senseRadius;

    float FlatDistanceSqr(Vector3 other)
    {
        Vector3 offset = other - transform.position;
        offset.z = 0f;
        return offset.sqrMagnitude;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 1f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, senseRadius);
    }
}
