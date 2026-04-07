using UnityEngine;

//all the different attack types, matching smash bros conventions
public enum AttackType
{
    None,

    //grounded attacks
    Jab,
    ForwardTilt,
    UpTilt,
    DownTilt,
    DashAttack,

    //smash attacks
    ForwardSmash,
    UpSmash,
    DownSmash,

    //aerial attacks
    NeutralAir,
    ForwardAir,
    BackAir,
    UpAir,
    DownAir
}

[System.Serializable]
public class AttackData
{
    public AttackType type;
    public Sprite sprite;             //sprite to display during this attack
    public float damage = 10f;
    public float knockback = 5f;
    public float duration = 0.3f;     //how long the attack lasts in seconds
    public Vector2 hitboxSize = new Vector2(1f, 1f);
    public Vector2 hitboxOffset = new Vector2(0.5f, 0f);

    //delay before the hitbox becomes active (startup frames basically)
    public float startupTime = 0.05f;

    //how long after the attack ends before you can act again
    public float endLag = 0.1f;
}

public class PlayerAttack : MonoBehaviour
{
    [Header("Attack Definitions")]
    //fill these out in the inspector with your sprites, damage values, etc.
    //
    //you need one entry per attack type you want to support
    public AttackData[] attacks;

    [Header("References")]
    public SpriteRenderer spriteRenderer;
    public Rigidbody2D rb;
    public LayerMask hittableLayers;

    [Header("Ground Detection")]
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public LayerMask groundLayer;

    [Header("Attack Key")]
    //change this in the inspector if you want a different attack button
    public KeyCode attackKey = KeyCode.Space;

    [Header("Smash Attack")]
    //hold a direction key and press attack within this window
    //after the direction key was first pressed to get a smash attack
    public float smashInputWindow = 0.12f;

    //state
    private bool isAttacking;
    private bool isGrounded;
    private bool isDashing;          //set this from your movement script
    private AttackType currentAttack;
    private float attackTimer;
    private float endLagTimer;
    private bool hitboxActive;

    //input tracking
    private float wPressTime = -1f;
    private float sPressTime = -1f;
    private float aPressTime = -1f;
    private float dPressTime = -1f;

    //cached
    private AttackData currentAttackData;
    private Sprite defaultSprite;

    //public properties so your movement script can check attack state
    public bool IsAttacking => isAttacking || endLagTimer > 0f;
    public AttackType CurrentAttack => currentAttack;

    void Start()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        if (spriteRenderer != null)
            defaultSprite = spriteRenderer.sprite;
    }

    void Update()
    {
        CheckGrounded();
        TrackDirectionKeyPresses();
        HandleAttackInput();
        UpdateAttackTimers();
    }

    //=== INPUT ===

    private void TrackDirectionKeyPresses()
    {
        //record the time each direction key was first pressed
        //so we can tell the difference between tilt and smash
        if (Input.GetKeyDown(KeyCode.W)) wPressTime = Time.time;
        if (Input.GetKeyDown(KeyCode.S)) sPressTime = Time.time;
        if (Input.GetKeyDown(KeyCode.A)) aPressTime = Time.time;
        if (Input.GetKeyDown(KeyCode.D)) dPressTime = Time.time;
    }

    private void HandleAttackInput()
    {
        if (!Input.GetKeyDown(attackKey)) return;
        if (isAttacking || endLagTimer > 0f) return;

        AttackType attackToPerform = DetermineAttackType();
        PerformAttack(attackToPerform);
    }

    //=== ATTACK TYPE DETERMINATION ===

    private AttackType DetermineAttackType()
    {
        if (isGrounded)
            return DetermineGroundedAttack();
        else
            return DetermineAerialAttack();
    }

    private AttackType DetermineGroundedAttack()
    {
        bool holdingLeft = Input.GetKey(KeyCode.A);
        bool holdingRight = Input.GetKey(KeyCode.D);
        bool holdingUp = Input.GetKey(KeyCode.W);
        bool holdingDown = Input.GetKey(KeyCode.S);
        bool holdingHorizontal = holdingLeft || holdingRight;

        //check for smash attacks — direction key was pressed very recently
        //meaning the player tapped direction + attack at roughly the same time
        if (holdingHorizontal && WasRecentlyPressed(holdingLeft ? aPressTime : dPressTime))
        {
            //dash attack if player is currently dashing
            if (isDashing)
                return AttackType.DashAttack;

            return AttackType.ForwardSmash;
        }

        if (holdingUp && WasRecentlyPressed(wPressTime))
            return AttackType.UpSmash;

        if (holdingDown && WasRecentlyPressed(sPressTime))
            return AttackType.DownSmash;

        //tilt attacks — direction is held but wasn't just pressed
        if (holdingHorizontal)
        {
            if (isDashing)
                return AttackType.DashAttack;

            return AttackType.ForwardTilt;
        }

        if (holdingUp)
            return AttackType.UpTilt;

        if (holdingDown)
            return AttackType.DownTilt;

        //no direction = jab
        return AttackType.Jab;
    }

    private AttackType DetermineAerialAttack()
    {
        bool holdingLeft = Input.GetKey(KeyCode.A);
        bool holdingRight = Input.GetKey(KeyCode.D);
        bool holdingUp = Input.GetKey(KeyCode.W);
        bool holdingDown = Input.GetKey(KeyCode.S);

        if (holdingLeft || holdingRight)
        {
            //figure out if this is forward or back based on facing direction
            float facingDir = transform.localScale.x >= 0 ? 1f : -1f;
            float inputDir = holdingRight ? 1f : -1f;

            return (inputDir == facingDir) ? AttackType.ForwardAir : AttackType.BackAir;
        }

        if (holdingUp)
            return AttackType.UpAir;

        if (holdingDown)
            return AttackType.DownAir;

        return AttackType.NeutralAir;
    }

    //returns true if the key was pressed within the smash input window
    private bool WasRecentlyPressed(float pressTime)
    {
        return (Time.time - pressTime) <= smashInputWindow;
    }

    //=== ATTACK EXECUTION ===

    private void PerformAttack(AttackType type)
    {
        currentAttackData = GetAttackData(type);

        //if we dont have data for this attack, fall back to jab/nair
        if (currentAttackData == null)
        {
            type = isGrounded ? AttackType.Jab : AttackType.NeutralAir;
            currentAttackData = GetAttackData(type);
        }

        //still nothing? bail out
        if (currentAttackData == null) return;

        isAttacking = true;
        currentAttack = type;
        attackTimer = currentAttackData.duration;
        hitboxActive = false;

        //swap sprite if one is assigned
        if (currentAttackData.sprite != null && spriteRenderer != null)
            spriteRenderer.sprite = currentAttackData.sprite;

        OnAttackStarted(type);
    }

    private void UpdateAttackTimers()
    {
        //handle end lag
        if (!isAttacking && endLagTimer > 0f)
        {
            endLagTimer -= Time.deltaTime;
            return;
        }

        if (!isAttacking) return;

        attackTimer -= Time.deltaTime;

        //activate hitbox after startup time
        if (!hitboxActive && currentAttackData != null &&
            (currentAttackData.duration - attackTimer) >= currentAttackData.startupTime)
        {
            hitboxActive = true;
            CheckHit();
        }

        //attack finished
        if (attackTimer <= 0f)
        {
            EndAttack();
        }
    }

    private void EndAttack()
    {
        float endLag = currentAttackData != null ? currentAttackData.endLag : 0f;

        isAttacking = false;
        hitboxActive = false;
        currentAttack = AttackType.None;
        endLagTimer = endLag;

        //restore default sprite
        if (spriteRenderer != null && defaultSprite != null)
            spriteRenderer.sprite = defaultSprite;

        currentAttackData = null;

        OnAttackEnded();
    }

    //=== HITBOX ===

    private void CheckHit()
    {
        if (currentAttackData == null) return;

        //flip hitbox offset based on facing direction
        float facingDir = transform.localScale.x >= 0 ? 1f : -1f;
        Vector2 offset = new Vector2(
            currentAttackData.hitboxOffset.x * facingDir,
            currentAttackData.hitboxOffset.y
        );

        Vector2 center = (Vector2)transform.position + offset;

        //check for hits using an overlap box
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            center,
            currentAttackData.hitboxSize,
            0f,
            hittableLayers
        );

        foreach (Collider2D hit in hits)
        {
            //dont hit ourselves
            if (hit.transform == transform) continue;
            if (hit.transform.IsChildOf(transform)) continue;

            OnHitTarget(hit, currentAttackData);
        }
    }

    //=== GROUND CHECK ===

    private void CheckGrounded()
    {
        if (groundCheck != null)
        {
            isGrounded = Physics2D.OverlapCircle(
                groundCheck.position,
                groundCheckRadius,
                groundLayer
            );
        }
    }

    //=== HELPERS ===

    private AttackData GetAttackData(AttackType type)
    {
        if (attacks == null) return null;

        foreach (AttackData data in attacks)
        {
            if (data.type == type)
                return data;
        }
        return null;
    }

    //call this from your movement script to let the attack system
    //know if the player is currently dashing
    public void SetDashing(bool dashing)
    {
        isDashing = dashing;
    }

    //call this from your movement script if you use a different
    //grounded check
    public void SetGrounded(bool grounded)
    {
        isGrounded = grounded;
    }

    //=== EVENTS ===
    //
    //override these or swap them for UnityEvents / C# events
    //depending on how you want to hook things up

    protected virtual void OnAttackStarted(AttackType type)
    {
        //hook up animations, sound effects, particles, etc.
        //Debug.Log($"Attack started: {type}");
    }

    protected virtual void OnAttackEnded()
    {
        //attack finished, player can act again
    }

    protected virtual void OnHitTarget(Collider2D target, AttackData attackData)
    {
        //deal damage and apply knockback via rigidbody
        //
        //example:
        //var health = target.GetComponent<Health>();
        //if (health != null) health.TakeDamage(attackData.damage);

        Rigidbody2D targetRb = target.GetComponent<Rigidbody2D>();
        if (targetRb != null)
        {
            Vector2 dir = (target.transform.position - transform.position).normalized;
            targetRb.AddForce(dir * attackData.knockback, ForceMode2D.Impulse);
        }

        Debug.Log($"Hit {target.name} with {attackData.type} for {attackData.damage} damage");
    }

    //=== DEBUG GIZMOS ===
    //
    //draws the hitbox in the scene view so you can see where attacks land

    void OnDrawGizmosSelected()
    {
        if (currentAttackData != null && hitboxActive)
        {
            Gizmos.color = Color.red;
        }
        else
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        }

        //show hitbox for whatever attack is selected in the inspector
        //or the current active attack
        AttackData dataToShow = currentAttackData;

        if (dataToShow == null && attacks != null && attacks.Length > 0)
            dataToShow = attacks[0];

        if (dataToShow != null)
        {
            float facingDir = transform.localScale.x >= 0 ? 1f : -1f;
            Vector2 offset = new Vector2(
                dataToShow.hitboxOffset.x * facingDir,
                dataToShow.hitboxOffset.y
            );

            Vector2 center = (Vector2)transform.position + offset;
            Gizmos.DrawWireCube(center, dataToShow.hitboxSize);
        }

        //ground check sphere
        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
