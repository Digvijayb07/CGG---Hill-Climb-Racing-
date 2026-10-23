using TMPro;
using UnityEngine;

public class GameOverUI : MonoBehaviour
{
    public static GameOverUI instance;

    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Live Text")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text distanceText;
    [SerializeField] private TMP_Text bestDistanceText;

    [Header("Optional Player Override")]
    [SerializeField] private Transform playerOverride;

    private void Awake()
    {
        instance = this;

        if (panelRoot == null)
            panelRoot = gameObject;

        panelRoot.SetActive(false);
    }

    public void ShowGameOver()
    {
        int coins = 0;
        if (ScoreManager.instance != null)
            coins = ScoreManager.instance.GetScore();

        int distance = Mathf.Max(0, Mathf.RoundToInt(GetPlayerDistance()));
        int bestDistance = Mathf.Max(distance, PlayerPrefs.GetInt("BEST_DISTANCE", 0));

        PlayerPrefs.SetInt("BEST_DISTANCE", bestDistance);
        PlayerPrefs.Save();

        if (coinsText != null)
            coinsText.text = coins.ToString();

        if (distanceText != null)
            distanceText.text = distance + " m";

        if (bestDistanceText != null)
            bestDistanceText.text = bestDistance + " m";

        panelRoot.SetActive(true);
    }

    public void HideGameOver()
    {
        panelRoot.SetActive(false);
    }

    private float GetPlayerDistance()
    {
        if (playerOverride != null)
            return playerOverride.position.x;

        Driver[] drivers = FindObjectsByType<Driver>(FindObjectsSortMode.None);

        foreach (Driver d in drivers)
        {
            if (d.gameObject.activeInHierarchy)
                return d.transform.position.x;
        }

        return 0f;
    }
}