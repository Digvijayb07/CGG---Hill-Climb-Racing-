using System.Collections.Generic;
using UnityEngine;

// Attach once to Ground. Feedback only; the terrain supplies the physical bump.
[DisallowMultipleComponent]
[RequireComponent(typeof(EnvironmentGenerator))]
public sealed class PotholeHazard : MonoBehaviour
{
    [SerializeField] private EnvironmentGenerator terrain;
    [SerializeField] private PotholeVisual visuals;
    [SerializeField, Min(0.1f)] private float minimumImpactSpeed = 1.2f;
    [SerializeField, Min(0.2f)] private float strongImpactSpeed = 5f;
    private readonly List<Collider2D> wheels = new List<Collider2D>();
    private readonly HashSet<int> reported = new HashSet<int>();
    private readonly HashSet<int> liveIds = new HashSet<int>();
    private readonly ContactPoint2D[] contacts = new ContactPoint2D[32];
    private Transform cachedPlayer;

    private void Awake()
    {
        if (terrain == null) terrain = GetComponent<EnvironmentGenerator>();
        if (visuals == null) visuals = GetComponent<PotholeVisual>();
    }

    private void CacheWheels()
    {
        cachedPlayer = terrain.Player;
        wheels.Clear();
        if (cachedPlayer == null) return;
        Rigidbody2D chassis = cachedPlayer.GetComponent<Rigidbody2D>();
        foreach (Rigidbody2D body in cachedPlayer.GetComponentsInChildren<Rigidbody2D>(true))
        {
            if (body == chassis) continue;
            foreach (Collider2D col in body.GetComponents<Collider2D>())
                if (!col.isTrigger && col.attachedRigidbody == body) wheels.Add(col);
        }
    }

    private void FixedUpdate()
    {
        if (terrain == null || !terrain.IsRunActive || terrain.GroundCollider == null) return;
        if (cachedPlayer != terrain.Player) CacheWheels();
        if (visuals == null) visuals = GetComponent<PotholeVisual>();
        if (visuals == null || !visuals.isActiveAndEnabled) return;

        liveIds.Clear();
        foreach (EnvironmentGenerator.Pothole hole in terrain.Potholes) liveIds.Add(hole.Id);
        reported.RemoveWhere(id => !liveIds.Contains(id));
        foreach (Collider2D wheel in wheels)
        {
            if (wheel == null || !wheel.enabled || !wheel.gameObject.activeInHierarchy) continue;
            int count = wheel.GetContacts(contacts);
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D contact = contacts[i];
                if (contact.collider != terrain.GroundCollider && contact.otherCollider != terrain.GroundCollider) continue;
                // Normal contact speed rejects ordinary rolling along the road.
                float impact = Mathf.Abs(Vector2.Dot(contact.relativeVelocity, contact.normal));
                if (impact < minimumImpactSpeed) continue;
                foreach (EnvironmentGenerator.Pothole hole in terrain.Potholes)
                {
                    if (hole.IsRepaired || reported.Contains(hole.Id) ||
                        contact.point.x < hole.LeftX || contact.point.x > hole.RightX) continue;
                    float strength = Mathf.InverseLerp(minimumImpactSpeed,
                        Mathf.Max(minimumImpactSpeed + 0.1f, strongImpactSpeed), impact);
                    visuals.PlayImpact(contact.point, Mathf.Lerp(0.15f, 1f, strength));
                    reported.Add(hole.Id);
                    break;
                }
            }
        }
    }
}
