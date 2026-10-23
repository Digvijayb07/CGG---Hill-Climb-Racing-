using UnityEngine;

// Light, speed-dependent dust at the actual rear-wheel contact point.
[DisallowMultipleComponent]
public class RearWheelDustFX : MonoBehaviour
{
    [SerializeField] private Collider2D wheelCollider;
    [SerializeField] private Collider2D groundCollider;
    [SerializeField] private Rigidbody2D wheelBody;
    [Tooltip("Optional existing particle system. Leave empty for the lightweight pooled dust.")]
    [SerializeField] private ParticleSystem dust;
    [SerializeField, Min(0f)] private float minimumHorizontalSpeed = 1.2f;
    [SerializeField, Range(2f, 16f)] private float maximumPuffsPerSecond = 10f;

    private readonly ContactPoint2D[] contacts = new ContactPoint2D[8];
    private Driver driver;
    private SpriteRenderer appearance;
    private float puffBudget;
    private bool emitting;

    private void Start()
    {
        if (wheelCollider == null) wheelCollider = GetComponent<Collider2D>();
        if (wheelBody == null) wheelBody = GetComponent<Rigidbody2D>();
        if (dust == null) dust = GetComponentInChildren<ParticleSystem>(true);
        driver = GetComponentInParent<Driver>();
        appearance = GetComponentInChildren<SpriteRenderer>();
        if (groundCollider == null)
        {
            EnvironmentGenerator terrain = FindFirstObjectByType<EnvironmentGenerator>();
            if (terrain != null) groundCollider = terrain.GetComponent<EdgeCollider2D>();
        }
        if (wheelCollider == null || groundCollider == null || wheelBody == null || (dust == null && appearance == null))
        {
            Debug.LogWarning("RearWheelDustFX: wheel body/collider, terrain collider and a sprite or particle system are required.", this);
            enabled = false;
            return;
        }
        if (dust != null)
        {
            var emission = dust.emission; emission.enabled = false;
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        else SubtleGameplayFX.WarmUp(this);
    }

    private void Update()
    {
        if (wheelBody == null || wheelCollider == null || groundCollider == null) return;
        float speed = wheelBody.linearVelocity.x;
        bool moving = Time.timeScale > 0f && wheelBody.simulated && wheelCollider.enabled &&
            (driver == null || driver.isActiveAndEnabled) && Mathf.Abs(speed) >= minimumHorizontalSpeed;
        bool grounded = false;
        Vector2 point = default;
        if (moving)
        {
            int count = wheelCollider.GetContacts(contacts);
            for (int i = 0; i < count; i++)
                if (contacts[i].collider == groundCollider || contacts[i].otherCollider == groundCollider)
                {
                    grounded = true;
                    point = contacts[i].point + Vector2.up * 0.06f;
                    break;
                }
        }
        if (dust != null)
        {
            if (grounded == emitting) return;
            emitting = grounded;
            var emission = dust.emission; emission.enabled = emitting;
            if (emitting) dust.Play();
            else dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            return;
        }
        if (!grounded) { puffBudget = 0f; return; }
        float rate = Mathf.Lerp(3f, maximumPuffsPerSecond, Mathf.InverseLerp(minimumHorizontalSpeed, 12f, Mathf.Abs(speed)));
        puffBudget += rate * Mathf.Min(Time.deltaTime, 0.05f);
        if (puffBudget < 1f) return;
        puffBudget -= 1f;
        SubtleGameplayFX.Dust(point, speed, appearance);
    }

    private void OnDisable()
    {
        puffBudget = 0f; emitting = false;
        if (dust == null) return;
        var emission = dust.emission; emission.enabled = false;
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
