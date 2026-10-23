using UnityEngine;

public class Driver : MonoBehaviour
{
    // Preserve the names used by the existing vehicle prefabs.
    [SerializeField] private Rigidbody2D _frontTireRB;
    [SerializeField] private Rigidbody2D _backTireRB;
    [SerializeField] private Rigidbody2D _carRB;
    [SerializeField] private float _speed = 150f;
    [SerializeField] private float _rotationSpeed;
    [SerializeField] private bool gasPressed;
    [SerializeField] private bool brakePressed;

    [Header("Control Response")]
    [Tooltip("Time to reach full throttle. Zero restores instant response. Release always cuts drive torque immediately.")]
    [SerializeField, Range(0f, 0.3f)] private float throttleRiseTime = 0.08f;

    [Header("Gentle Rotation")]
    [SerializeField, Range(0f, 0.3f)] private float rotationRiseTime = 0.14f;
    [Tooltip("Multiplier on the existing Rotation Speed. Wheel power is unchanged.")]
    [SerializeField, Range(0.5f, 1f)] private float rotationStrength = 0.85f;
    [Tooltip("Input torque eases off near this spin speed. Collisions can still spin the car faster; counter-steering remains available.")]
    [SerializeField, Min(60f)] private float rotationSoftLimit = 240f;
    [SerializeField] private bool smoothBodyMotion = true;

    [Header("Subtle Visual Feedback")]
    [SerializeField] private bool enableDustTrail = true;

    // Preserve the original force strength tuned at a 50 Hz physics timestep.
    // Unity integrates Force torque itself; its magnitude must not depend on fixedDeltaTime.
    private const float LegacyTorqueScale = 0.02f;
    private float keyboardInput;
    private float _moveInput;
    private float rotationInput;
    private bool focusLost, appPaused;
    public float DriveInput => _moveInput;

    private void Awake()
    {
        if (_carRB == null) _carRB = GetComponent<Rigidbody2D>();
        if (_carRB == null || _frontTireRB == null || _backTireRB == null)
        {
            Debug.LogError("Driver: assign the chassis and both wheel Rigidbody2D references.", this);
            enabled = false;
            return;
        }
        // Match the already-interpolated chassis so wheels/head don't visibly jitter
        // against it between physics ticks. Explicit existing modes are preserved.
        if (smoothBodyMotion)
            foreach (Rigidbody2D body in GetComponentsInChildren<Rigidbody2D>(true))
                if (body.interpolation == RigidbodyInterpolation2D.None)
                    body.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void Start()
    {
        SubtleGameplayFX.WarmUp(this);
        if (enableDustTrail && _backTireRB != null && _backTireRB.GetComponent<RearWheelDustFX>() == null)
            _backTireRB.gameObject.AddComponent<RearWheelDustFX>();
    }

    private void Update()
    {
        if (focusLost || appPaused || Time.timeScale <= 0f || _carRB == null || !_carRB.simulated)
        {
            ResetInput();
            return;
        }
        keyboardInput = Input.GetAxisRaw("Horizontal");
    }

    private void FixedUpdate()
    {
        if (focusLost || appPaused || Time.timeScale <= 0f || _carRB == null || !_carRB.simulated) return;
        float requested = ResolveInput(keyboardInput, gasPressed, brakePressed);
        _moveInput = StepInput(_moveInput, requested, throttleRiseTime, Time.fixedDeltaTime);
        rotationInput = StepInput(rotationInput, requested, rotationRiseTime, Time.fixedDeltaTime);
        if (_moveInput == 0f) return;

        float wheelTorque = -_moveInput * _speed * LegacyTorqueScale;
        if (_frontTireRB != null && _frontTireRB.simulated) _frontTireRB.AddTorque(wheelTorque, ForceMode2D.Force);
        if (_backTireRB != null && _backTireRB.simulated) _backTireRB.AddTorque(wheelTorque, ForceMode2D.Force);
        float rotationTorque = -rotationInput * _rotationSpeed * LegacyTorqueScale * rotationStrength;
        rotationTorque = EaseRotationTorque(rotationTorque, _carRB.angularVelocity, rotationSoftLimit);
        _carRB.AddTorque(rotationTorque, ForceMode2D.Force);
    }

    internal static float EaseRotationTorque(float torque, float angularVelocity, float limit)
    {
        if (torque == 0f) return 0f;
        limit = Mathf.Max(60f, limit);
        float speedInRequestedDirection = angularVelocity * Mathf.Sign(torque);
        // Only reduce input that increases an already-fast spin; never clamp velocity
        // or weaken the player's ability to counter-steer after a rough landing.
        float amount = Mathf.InverseLerp(limit * 0.65f, limit, speedInRequestedDirection);
        return torque * (1f - Mathf.SmoothStep(0f, 1f, amount));
    }

    internal static float ResolveInput(float keyboard, bool gas, bool brake)
    {
        // Conflicting touch pedals cancel; one touch pedal takes priority over the keyboard.
        if (gas || brake) return gas == brake ? 0f : gas ? 1f : -1f;
        return Mathf.Clamp(keyboard, -1f, 1f);
    }

    internal static float StepInput(float current, float requested, float riseTime, float deltaTime)
    {
        if (requested == 0f || riseTime <= 0f) return requested;
        // Do not apply leftover forward torque after the player asks to brake/reverse.
        if (current * requested < 0f) current = 0f;
        return Mathf.MoveTowards(current, requested, Mathf.Max(0f, deltaTime) / riseTime);
    }

    public void GasDown() { if (CanAcceptPedal()) gasPressed = true; }
    public void GasUp() { gasPressed = false; }
    public void BrakeDown() { if (CanAcceptPedal()) brakePressed = true; }
    public void BrakeUp() { brakePressed = false; }
    private bool CanAcceptPedal() => isActiveAndEnabled && !focusLost && !appPaused && Time.timeScale > 0f;

    public void ResetInput()
    {
        gasPressed = brakePressed = false;
        keyboardInput = _moveInput = rotationInput = 0f;
    }

    private void OnDisable() => ResetInput();
    private void OnApplicationFocus(bool focused)
    {
        focusLost = !focused;
        if (!focused) ResetInput();
    }
    private void OnApplicationPause(bool paused)
    {
        appPaused = paused;
        if (paused) ResetInput();
    }
}
