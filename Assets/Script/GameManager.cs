
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;

    [Header("Game Over UI")]
    [SerializeField] private GameObject gameOverPanel;

    private bool isGameOver = false;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        Time.timeScale = 1f;
        isGameOver = false;

        // Hide the Restart screen initially.
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);
    }

    public void GameOver()
    {
        if (isGameOver) return;

        isGameOver = true;

        Debug.Log("GAME OVER TRIGGERED!");

        // Show restart UI.
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
        else
        {
            Debug.LogError(
                "GameOver Panel is not assigned in GameManager!"
            );
        }

        // Pause gameplay.
        Time.timeScale = 0f;
    }

    public void RestartGame()
    {
        // Resume time before reloading.
        Time.timeScale = 1f;

        // Reload current scene.
        SceneManager.LoadScene(
            SceneManager.GetActiveScene().name
        );
    }

    public void Restart()
    {
        RestartGame();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
