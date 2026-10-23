using UnityEngine;

public class Collectible : MonoBehaviour
{
    [SerializeField] private int score = 10;
    private bool collected;
    private void OnEnable() => collected = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collected || Time.timeScale <= 0f || !collision.CompareTag("Player") || ScoreManager.instance == null) return;
        // Destroy is deferred: a second wheel/chassis callback can arrive this frame.
        collected = true;
        ScoreManager.instance.AddPoints(score);
        SubtleGameplayFX.Pickup(transform.position, GetComponentInChildren<SpriteRenderer>(), false);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
