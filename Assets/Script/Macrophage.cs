using UnityEngine;

// The macrophage "brain". Decides where to go and tells CellMovement to walk there.
// Priority (highest first): player order > chase pathogen (chemotaxis) > wander (patrol tissue).
[RequireComponent(typeof(CellMovement))]
public class Macrophage : MonoBehaviour
{
    public enum State { Idle, Wandering, Commanded, Chasing, Engulfing }

    [Header("Chemotaxis (sensing pathogens)")]
    [SerializeField] float senseRadius = 5f;
    [Tooltip("How often (seconds) to re-path while chasing, since the target may move.")]
    [SerializeField] float repathInterval = 0.5f;

    [Header("Phagocytosis")]
    [Tooltip("Damage per second dealt to the pathogen while engulfing/digesting it.")]
    [SerializeField] float digestDamagePerSecond = 25f;

    [Header("Wandering")]
    [SerializeField] int wanderRadius = 4;
    [SerializeField] Vector2 idleTimeRange = new Vector2(1f, 3f);

    public static readonly System.Collections.Generic.List<Macrophage> All = new System.Collections.Generic.List<Macrophage>();

    public State CurrentState { get; private set; } = State.Idle;
    public CellHealth Health { get; private set; }

    // State names in the Animator Controller on the Visual child. No parameters or transitions needed.
    const string IdleAnim = "Idle";
    const string WalkAnim = "Walk";
    const string AttackAnim = "Attack";

    CellMovement movement;
    Animator animator;
    string playingAnim; // null = nothing played yet
    Pathogen target;
    float timer;

    void Awake()
    {
        movement = GetComponent<CellMovement>();
        movement.Arrived += OnArrived;
        animator = CellMovement.FindOnVisual<Animator>(this);
        if (GetComponent<Animator>() != null || GetComponent<SpriteRenderer>() != null)
            Debug.LogWarning(name + " has a Sprite Renderer or Animator on its root object. Remove them there and keep " +
                             "them only on the Visual child, or animations will stop it from moving.", this);
        Health = GetComponent<CellHealth>();
        if (Health == null) Health = gameObject.AddComponent<CellHealth>();
        if (GetComponent<Selectable>() == null) gameObject.AddComponent<Selectable>();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        // CellMovement turns itself off if the scene has no TissueGrid; it has already logged why.
        if (!movement.enabled) return;

        switch (CurrentState)
        {
            case State.Idle:
                if (TryStartChase()) break;
                timer -= Time.deltaTime;
                if (timer <= 0f) StartWander();
                break;

            case State.Wandering:
                TryStartChase();
                break;

            case State.Commanded:
                // Follow the player's order; ignore pathogens until we arrive.
                break;

            case State.Chasing:
                UpdateChase();
                break;

            case State.Engulfing:
                UpdateEngulf();
                break;
        }

        UpdateAnimation();
    }

    // Attack while engulfing a pathogen, Walk while moving between tiles, Idle otherwise.
    void UpdateAnimation()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        string anim = CurrentState == State.Engulfing ? AttackAnim
                    : movement.IsMoving ? WalkAnim
                    : IdleAnim;
        if (playingAnim == anim) return; // only switch when it changes, so the clip isn't restarted every frame

        playingAnim = anim; // set first so a missing state warns once, not every frame
        int hash = Animator.StringToHash(anim);
        if (!animator.HasState(0, hash))
        {
            Debug.LogWarning(name + ": the Animator Controller has no state named \"" + anim + "\".", this);
            return;
        }
        animator.Play(hash, 0, 0f);
    }

    // Called by MacrophageCommander when the player right-clicks a tile.
    public bool CommandMoveTo(Vector3Int cell)
    {
        ReleaseTarget();
        if (!movement.MoveTo(cell)) return false;
        CurrentState = State.Commanded;
        return true;
    }

    // Called by MacrophageCommander when the player right-clicks a pathogen.
    public void CommandAttack(Pathogen pathogen)
    {
        ReleaseTarget();
        movement.Stop();
        target = pathogen;
        target.ClaimedBy = this;
        CurrentState = State.Chasing;
        timer = 0f; // re-path immediately
        Debug.Log(name + " ordered to attack " + pathogen.name + ".", this);
    }

    void OnArrived()
    {
        if (CurrentState == State.Commanded || CurrentState == State.Wandering)
            GoIdle();
    }

    void GoIdle()
    {
        CurrentState = State.Idle;
        timer = Random.Range(idleTimeRange.x, idleTimeRange.y);
    }

    void StartWander()
    {
        var grid = TissueGrid.Instance;
        Vector3Int here = movement.CurrentCell;

        // Try a few random cells nearby; give up quietly if we're boxed in.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var offset = new Vector3Int(Random.Range(-wanderRadius, wanderRadius + 1),
                                        Random.Range(-wanderRadius, wanderRadius + 1), 0);
            if (offset == Vector3Int.zero || !grid.IsWalkable(here + offset)) continue;
            if (movement.MoveTo(here + offset))
            {
                CurrentState = State.Wandering;
                return;
            }
        }
        GoIdle();
    }

    bool TryStartChase()
    {
        Pathogen nearest = FindNearestPathogen();
        if (nearest == null) return false;

        target = nearest;
        target.ClaimedBy = this;
        Debug.Log(name + " senses " + target.name + " and starts chasing it (chemotaxis).", this);
        CurrentState = State.Chasing;
        timer = 0f; // re-path immediately
        return true;
    }

    Pathogen FindNearestPathogen()
    {
        Pathogen best = null;
        float bestDist = senseRadius * senseRadius;
        foreach (var p in Pathogen.All)
        {
            if (p.ClaimedBy != null && p.ClaimedBy != this) continue;
            Vector3 offset = p.transform.position - transform.position;
            offset.z = 0f; // ignore sprite depth differences
            float d = offset.sqrMagnitude;
            if (d <= bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    void UpdateChase()
    {
        if (target == null) // eaten by something else or destroyed
        {
            movement.Stop();
            GoIdle();
            return;
        }

        Vector3Int targetCell = TissueGrid.Instance.WorldToCell(target.transform.position);

        if (IsAdjacentTo(target))
        {
            movement.Stop();
            CurrentState = State.Engulfing;
            Debug.Log(name + " is engulfing " + target.name + " (phagocytosis).", this);
            return;
        }

        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = repathInterval;

        if (!movement.MoveNextTo(targetCell))
        {
            // Unreachable (behind a wall). Let it go so another macrophage can try.
            Debug.Log(name + " can't reach " + target.name + " (blocked by walls or no floor tiles).", this);
            ReleaseTarget();
            GoIdle();
        }
    }

    void UpdateEngulf()
    {
        if (target == null)
        {
            GoIdle();
            return;
        }

        // Pathogen moved away -> chase it again.
        if (!IsAdjacentTo(target))
        {
            CurrentState = State.Chasing;
            timer = 0f;
            return;
        }

        movement.FaceTowards(target.transform.position);

        // Digest it over time. Meanwhile its toxins hurt us, so a weak macrophage can lose the fight.
        target.Health.TakeDamage(digestDamagePerSecond * Time.deltaTime);
        if (target == null || target.Health.IsDead)
        {
            target = null;
            GoIdle();
        }
    }

    // Same tile or one of the 8 tiles around it.
    bool IsAdjacentTo(Pathogen pathogen)
    {
        Vector3Int a = TissueGrid.Instance.WorldToCell(pathogen.transform.position);
        return TissueGrid.CellDistance(a, movement.CurrentCell) <= 1;
    }

    void ReleaseTarget()
    {
        if (target != null && target.ClaimedBy == this) target.ClaimedBy = null;
        target = null;
    }

    void OnDestroy()
    {
        ReleaseTarget();
        if (movement != null) movement.Arrived -= OnArrived;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, senseRadius);
    }
}
