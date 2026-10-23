
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class VehicleSelector : MonoBehaviour
{
    [Header("Vehicles")]
    public GameObject[] vehicles;

    [Header("UI Elements")]
    public GameObject selectionPanel;
    public Button leftButton;
    public Button rightButton;
    public Button playButton;

    private Driver carDriver;
    private Rigidbody2D[] carBodies;
    private bool gameStarted = false;
    private readonly List<PedalReleaseBinding> pedalReleaseBindings = new List<PedalReleaseBinding>();
    private struct PedalReleaseBinding
    {
        public EventTrigger trigger;
        public EventTrigger.Entry entry;
    }

    private void Awake()
    {
        // Some existing scenes store prefab assets here instead of Hierarchy objects.
        // Controls and feedback must use live Drivers; prefab assets are never active.
        Driver[] sceneDrivers = FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (vehicles != null)
            for (int i = 0; i < vehicles.Length; i++)
                vehicles[i] = ResolveSceneVehicle(vehicles[i], sceneDrivers);

        if (vehicles == null || vehicles.Length == 0 ||
            vehicles[0] == null)
        {
            Debug.LogError("VehicleSelector: Element 0 needs a scene vehicle with Driver. A prefab reference can resolve only to one matching vehicle in this scene.", this);
            enabled = false;
            return;
        }

        // Use only the red car.
        for (int i = 1; i < vehicles.Length; i++)
        {
            if (vehicles[i] != null)
                vehicles[i].SetActive(false);
        }

        GameObject car = vehicles[0];
        car.SetActive(true);

        carDriver = car.GetComponent<Driver>();

        if (carDriver != null)
        {
            carDriver.GasUp();
            carDriver.BrakeUp();
            carDriver.enabled = false;
        }

        // Freeze chassis AND wheels before START.
        carBodies = car.GetComponentsInChildren<Rigidbody2D>(true);

        foreach (Rigidbody2D rb in carBodies)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }
    }

    private GameObject ResolveSceneVehicle(GameObject configured, Driver[] candidates)
    {
        if (configured == null) return null;
        if (configured.scene.IsValid() && configured.scene.isLoaded) return configured;

        GameObject match = null;
        foreach (Driver candidate in candidates)
        {
            if (candidate == null || candidate.gameObject.scene != gameObject.scene) continue;
            string candidateName = candidate.gameObject.name;
            if (candidateName != configured.name && candidateName != configured.name + "(Clone)") continue;
            // Ambiguous prefab instances need an explicit Hierarchy reference.
            if (match != null) return null;
            match = candidate.gameObject;
        }
        return match;
    }

    private void Start()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(true);

        if (leftButton != null)
            leftButton.gameObject.SetActive(false);

        if (rightButton != null)
            rightButton.gameObject.SetActive(false);

        if (playButton != null)
        {
            // Connect START only once through code.
            playButton.onClick.RemoveListener(StartGame);
            playButton.onClick.AddListener(StartGame);
        }

        if (FuelManager.instance != null)
            FuelManager.instance.StopFuelDrain();

        BindPedalReleaseGuards();
    }

    public void StartGame()
    {
        if (gameStarted || carBodies == null) return;

        gameStarted = true;

        // Restore the car's physics.
        foreach (Rigidbody2D rb in carBodies)
        {
            if (rb != null)
                rb.simulated = true;
        }

        if (carDriver != null)
        {
            carDriver.GasUp();
            carDriver.BrakeUp();
            carDriver.enabled = true;
        }

        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        if (FuelManager.instance != null)
            FuelManager.instance.StartFuelDrain();
        else
            Debug.LogError("FuelManager is missing!");
    }

    // Gas and brake only work after START.

    public void GasDown()
    {
        if (gameStarted && carDriver != null)
            carDriver.GasDown();
    }

    public void GasUp()
    {
        if (carDriver != null)
            carDriver.GasUp();
    }

    public void BrakeDown()
    {
        if (gameStarted && carDriver != null)
            carDriver.BrakeDown();
    }

    public void BrakeUp()
    {
        if (carDriver != null)
            carDriver.BrakeUp();
    }

    // Extend existing scene wiring: sliding off a pedal releases it.
    // Only match this selector's callbacks, leaving unrelated EventTriggers alone.
    private void BindPedalReleaseGuards()
    {
        foreach (EventTrigger trigger in FindObjectsByType<EventTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool gas = false, brake = false;
            foreach (EventTrigger.Entry entry in trigger.triggers)
            {
                if (entry.eventID != EventTriggerType.PointerDown) continue;
                for (int i = 0; i < entry.callback.GetPersistentEventCount(); i++)
                {
                    if (entry.callback.GetPersistentTarget(i) != this) continue;
                    string method = entry.callback.GetPersistentMethodName(i);
                    gas |= method == nameof(GasDown);
                    brake |= method == nameof(BrakeDown);
                }
            }
            if (!gas && !brake) continue;
            PedalPressFX feedback = trigger.GetComponent<PedalPressFX>();
            if (feedback == null) feedback = trigger.gameObject.AddComponent<PedalPressFX>();
            feedback.Configure(gas ? PedalPressFX.PedalType.Gas : PedalPressFX.PedalType.Brake, carDriver);
            AddReleaseGuard(trigger, EventTriggerType.PointerExit, gas, brake);
            AddReleaseGuard(trigger, EventTriggerType.Cancel, gas, brake);
        }
    }

    private void AddReleaseGuard(EventTrigger trigger, EventTriggerType type, bool gas, bool brake)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ =>
        {
            if (gas) GasUp();
            if (brake) BrakeUp();
        });
        trigger.triggers.Add(entry);
        pedalReleaseBindings.Add(new PedalReleaseBinding { trigger = trigger, entry = entry });
    }

    private void OnDisable()
    {
        if (carDriver != null) carDriver.ResetInput();
    }

    private void OnDestroy()
    {
        foreach (PedalReleaseBinding binding in pedalReleaseBindings)
            if (binding.trigger != null) binding.trigger.triggers.Remove(binding.entry);
        pedalReleaseBindings.Clear();
        if (playButton != null)
            playButton.onClick.RemoveListener(StartGame);
    }
}
