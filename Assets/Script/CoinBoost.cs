using UnityEngine;

/// <summary>
/// Optional coin-powered acceleration for the existing 2D car.
/// Install on the vehicle root (the GameObject with Driver and its body Rigidbody2D).
/// Requires the existing ScoreManager.TrySpendCoins(int) method.
/// Ground contact is checked at the wheels before spending, so B cannot turn
/// into an air-thrust / flying-car ability.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Driver))]
public class CoinBoost : MonoBehaviour
{
    [Header("Connect ONE scene reference")]
    [Tooltip("Drag Ground from the Hierarchy here: its EdgeCollider2D.")]
    [SerializeField] private Collider2D groundCollider;

    [Header("Coin and boost balance")]
    [SerializeField, Min(1)] private int boostCost = 25;
    [SerializeField, Min(0.2f)] private float boostDuration = 3f;
    [SerializeField, Min(0f)] private float cooldownAfterBoost = 2f;
    [Tooltip("Horizontal acceleration in world units/s squared; independent of body mass.")]
    [SerializeField, Min(0.1f)] private float boostAcceleration = 8f;
    [Tooltip("Stops adding extra force once the car body reaches this horizontal speed.")]
    [SerializeField, Min(1f)] private float maximumBoostSpeed = 22f;
    [SerializeField] private bool showSmallHUD = true;

    private Driver driver;
    private Rigidbody2D carBody;
    private Rigidbody2D[] vehicleBodies;
    private Collider2D[] wheelColliders;
    private bool boosting;
    private float boostEndsAt;
    private float nextBoostAllowedAt;
    private float noticeEndsAt;
    private string notice;

    // Read-only state for the optional illustrated HUD. The physics/timers are unchanged.
    public int BoostCost => boostCost;
    public float BoostDuration => boostDuration;
    public float CooldownDuration => cooldownAfterBoost;
    public bool IsBoosting => boosting && Time.time < boostEndsAt;
    public float BoostTimeRemaining => Mathf.Max(0f, boostEndsAt - Time.time);
    public float CooldownTimeRemaining => Mathf.Max(0f, nextBoostAllowedAt - Time.time);
    public bool CanDisplayHUD => CanDrive();
    public bool LegacyHUDVisible => showSmallHUD;
    public string CurrentNotice => Time.time < noticeEndsAt ? notice : string.Empty;
    public void SetLegacyHUDVisible(bool visible) => showSmallHUD = visible;

    private void Awake()
    {
        driver = GetComponent<Driver>();
        carBody = GetComponent<Rigidbody2D>();
        vehicleBodies = GetComponentsInChildren<Rigidbody2D>(true);

        // Wheel colliders are the colliders on CHILD Rigidbody2D objects,
        // not the chassis. This avoids treating a high airborne chassis as grounded.
        System.Collections.Generic.List<Collider2D> wheels =
            new System.Collections.Generic.List<Collider2D>();
        foreach (Rigidbody2D body in vehicleBodies)
        {
            if (body == null || body == carBody) continue;
            foreach (Collider2D col in body.GetComponents<Collider2D>())
                if (col != null && !col.isTrigger) wheels.Add(col);
        }
        wheelColliders = wheels.ToArray();

        if (carBody == null)
            Debug.LogError("CoinBoost: vehicle root needs its car Rigidbody2D.", this);
        if (wheelColliders.Length == 0)
            Debug.LogError("CoinBoost: no wheel colliders found on child Rigidbody2D objects.", this);
    }

    private bool CanDrive()
    {
        // VehicleSelector enables the selected Driver only AFTER START.
        return isActiveAndEnabled && driver != null && driver.isActiveAndEnabled &&
               carBody != null && carBody.simulated && Time.timeScale > 0f;
    }

    private bool IsOnGround()
    {
        if (groundCollider == null || !groundCollider.enabled) return false;
        foreach (Collider2D wheel in wheelColliders)
            if (wheel != null && wheel.enabled && wheel.IsTouching(groundCollider))
                return true;
        return false;
    }

    private void Update()
    {
        if (boosting && Time.time >= boostEndsAt)
            boosting = false;

        if (CanDrive() && Input.GetKeyDown(KeyCode.B))
            TryBoost();
    }

    // Public for an optional UI button later: no additional scripts needed.
    public void TryBoost()
    {
        if (!CanDrive() || boosting || Time.time < nextBoostAllowedAt) return;

        if (groundCollider == null)
        {
            ShowNotice("ASSIGN GROUND COLLIDER");
            return;
        }
        if (!IsOnGround())
        {
            ShowNotice("LAND BEFORE BOOSTING");
            return;
        }
        if (ScoreManager.instance == null ||
            !ScoreManager.instance.TrySpendCoins(boostCost))
        {
            ShowNotice("NOT ENOUGH COINS");
            return;
        }

        boosting = true;
        boostEndsAt = Time.time + boostDuration;
        nextBoostAllowedAt = boostEndsAt + cooldownAfterBoost;
        Debug.Log("COIN BOOST: spent " + boostCost + " coins for " +
                  boostDuration + " seconds of extra acceleration.", this);
    }

    private void FixedUpdate()
    {
        if (!CanDrive() || !boosting || Time.time >= boostEndsAt ||
            driver.DriveInput < -0.01f || !IsOnGround() || carBody.linearVelocity.x >= maximumBoostSpeed)
            return;

        // Pure horizontal force: no vertical impulse and no additional torque.
        // Applying mass-scaled force to chassis AND wheels gives each connected
        // body the same acceleration instead of yanking the wheel joints apart.
        foreach (Rigidbody2D body in vehicleBodies)
        {
            if (body == null || !body.simulated ||
                body.bodyType != RigidbodyType2D.Dynamic) continue;
            body.AddForce(Vector2.right * (boostAcceleration * body.mass),
                          ForceMode2D.Force);
        }
    }

    private void ShowNotice(string message)
    {
        notice = message;
        noticeEndsAt = Time.time + 1.4f;
    }

    private void OnGUI()
    {
        if (!showSmallHUD || !CanDrive()) return;

        string caption;
        if (boosting)
            caption = "BOOST ACTIVE  " + Mathf.Max(0f, boostEndsAt - Time.time).ToString("0.0") + "s";
        else if (Time.time < noticeEndsAt)
            caption = notice;
        else if (Time.time < nextBoostAllowedAt)
            caption = "BOOST READY IN  " + (nextBoostAllowedAt - Time.time).ToString("0.0") + "s";
        else
            caption = "[B] BOOST  -" + boostCost + " COINS";

        float width = 210f;
        // Top-right, below existing warning/score elements; does not take input.
        Rect box = new Rect(Screen.width - width - 10f, 82f, width, 34f);
        GUI.Box(box, caption);
    }
}
