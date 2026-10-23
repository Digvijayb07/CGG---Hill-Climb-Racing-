using System.Globalization;
using TMPro;
using UnityEngine;

// Attach to the existing GameOver object, which GameManager enables on death.
// This only fills the blank value areas in the Game Over PNG.
public class GameOverStatsUI : MonoBehaviour
{
    [Header("Three text labels inside GameOver")]
    [SerializeField] private TMP_Text coinsValue;
    [SerializeField] private TMP_Text distanceValue;
    [SerializeField] private TMP_Text bestDistanceValue;

    [Header("Existing gameplay distance label")]
    [Tooltip("Drag UI > DistanceHUD > DistanceText here.")]
    [SerializeField] private TMP_Text gameplayDistanceText;

    private const string BestDistanceKey = "HCR_BEST_DISTANCE";

    private void OnEnable()
    {
        if (!Application.isPlaying) return;

        int balance = ScoreManager.instance != null ? ScoreManager.instance.GetScore() : 0;
        if (coinsValue != null) coinsValue.text = balance.ToString();

        int meters = ReadDistance();
        int bestMeters = Mathf.Max(meters, PlayerPrefs.GetInt(BestDistanceKey, 0));
        PlayerPrefs.SetInt(BestDistanceKey, bestMeters);
        PlayerPrefs.Save();

        if (distanceValue != null) distanceValue.text = meters + " m";
        if (bestDistanceValue != null) bestDistanceValue.text = bestMeters + " m";
    }

    private int ReadDistance()
    {
        if (gameplayDistanceText == null)
        {
            Debug.LogWarning("GameOverStatsUI: assign your existing DistanceHUD's DistanceText.", this);
            return 0;
        }

        // Supports normal HUD formats such as "245m", "245 m" and "245.5 m".
        string distanceString = gameplayDistanceText.text.Replace("m", "").Replace("M", "").Trim();
        if (float.TryParse(distanceString, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float meters))
            return Mathf.Max(0, Mathf.FloorToInt(meters));

        Debug.LogWarning("GameOverStatsUI: couldn't read distance from '" + gameplayDistanceText.text + "'.", this);
        return 0;
    }
}
