using UnityEngine;

public class CollectFuel : MonoBehaviour
{
    private bool collected;
    private void OnEnable() => collected = false;
   private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collected || Time.timeScale <= 0f || !collision.CompareTag("Player") || FuelManager.instance == null) return;
        collected = true;
        FuelManager.instance.FillFuel();
        SubtleGameplayFX.Pickup(transform.position, GetComponentInChildren<SpriteRenderer>(), true);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
