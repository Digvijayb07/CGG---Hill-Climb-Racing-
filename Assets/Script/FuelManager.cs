using UnityEngine;
using UnityEngine.UI;

public class FuelManager : MonoBehaviour
{
    public static FuelManager instance;

    [SerializeField] private Image fuelImage;
    [SerializeField, Range(0.1f, 5f)] private float fuelDrainSpeed = 1f;
    [SerializeField] private float maxFuel = 100f;
    [SerializeField] private Gradient fuelGradient;

    private float currentFuelAmount;
    private bool fuelDrainStarted = false;
    private float nextUIRefresh;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            enabled = false;
            return;
        }
        instance = this;
        currentFuelAmount = Mathf.Max(0f, maxFuel);
    }

    private void Start()
    {
        UpdateUI();
    }

    private void Update()
    {
        if (!fuelDrainStarted || Time.timeScale <= 0f) return;
        currentFuelAmount = Mathf.Max(0f, currentFuelAmount - Mathf.Max(0f, fuelDrainSpeed) * Time.deltaTime);
        if (Time.unscaledTime >= nextUIRefresh || currentFuelAmount <= 0f) UpdateUI();
        CheckEmpty();
    }

    private void UpdateUI()
    {
        nextUIRefresh = Time.unscaledTime + 0.1f;
        if (fuelImage == null) return;
        fuelImage.fillAmount = maxFuel > 0f ? currentFuelAmount / maxFuel : 0f;
        fuelImage.color = fuelGradient.Evaluate(fuelImage.fillAmount);
    }

    private void CheckEmpty()
    {
        if (currentFuelAmount > 0f || !fuelDrainStarted) return;
        fuelDrainStarted = false;
        if (GameManager.instance != null) GameManager.instance.GameOver();
    }

    public void StartFuelDrain() { fuelDrainStarted = true; }
    public void StopFuelDrain() { fuelDrainStarted = false; }

    public void FillFuel()
    {
        currentFuelAmount = maxFuel;
        UpdateUI();
    }

    // Fraction of the full tank, not current fuel.
    public void ApplyPotholePenalty(float fractionOfMaxFuel)
    {
        if (!fuelDrainStarted) return; // Never penalize the home/START screen.
        currentFuelAmount = Mathf.Max(0f,
            currentFuelAmount - maxFuel * Mathf.Clamp01(fractionOfMaxFuel));
        UpdateUI();
        CheckEmpty();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
