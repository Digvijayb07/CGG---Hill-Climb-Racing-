using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Uses the existing Button image and Sprite State assets. Never replaces the hit surface.
[DisallowMultipleComponent]
public class PedalPressFX : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, ICancelHandler
{
    public enum PedalType { Gas, Brake }
    [SerializeField] private PedalType pedal = PedalType.Gas;
    [SerializeField] private RectTransform pedalVisual;
    [Tooltip("Scale feedback is used only if the Button already targets separate child artwork. The button itself never shrinks.")]
    [SerializeField, Range(0.75f, 1f)] private float pressedScale = 0.95f;
    [SerializeField, Range(1f, 40f)] private float animationSpeed = 22f;
    [SerializeField] private Color pressedTint = new Color(0.88f, 0.92f, 0.96f, 1f);

    private readonly HashSet<int> heldPointers = new HashSet<int>();
    private Image pedalImage;
    private Vector3 normalScale;
    private Color normalTint;
    private Driver driver;
    private Button button;
    private bool focusLost, paused, scaleChildArtwork;

    public void Configure(PedalType type, Driver target) { pedal = type; driver = target; }

    private void Awake()
    {
        button = GetComponent<Button>();
        // Preserve the Inspector's Target Graphic and the normal Image.sprite.
        pedalImage = button != null ? button.targetGraphic as Image : null;
        if (pedalImage == null && pedalVisual != null) pedalImage = pedalVisual.GetComponent<Image>();
        if (pedalImage == null) pedalImage = GetComponent<Image>();
        if (pedalImage == null)
        {
            Debug.LogWarning("PedalPressFX: an Image is required for pedal feedback.", this);
            enabled = false;
            return;
        }
        pedalVisual = pedalImage.rectTransform;
        scaleChildArtwork = pedalVisual != transform && pedalVisual.IsChildOf(transform);
        normalScale = pedalVisual.localScale;
        normalTint = pedalImage.color;
    }

    private bool CanAnimate => isActiveAndEnabled && !focusLost && !paused && Time.timeScale > 0f &&
        (driver == null || driver.isActiveAndEnabled) &&
        (button == null || (button.isActiveAndEnabled && button.IsInteractable()));

    public void OnPointerDown(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || !CanAnimate) return;
        heldPointers.Add(data.pointerId);
        ApplySprite(true);
    }
    public void OnPointerUp(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left) return;
        heldPointers.Remove(data.pointerId);
        ApplySprite(CanAnimate && heldPointers.Count > 0);
    }
    public void OnPointerExit(PointerEventData data)
    {
        heldPointers.Remove(data.pointerId);
        ApplySprite(CanAnimate && heldPointers.Count > 0);
    }
    public void OnCancel(BaseEventData data) => ResetVisual();

    private void LateUpdate()
    {
        // Run after EventSystem/Selectable updates so selection or hover cannot leave
        // a released pedal showing the pressed artwork (and keyboard gets the same art).
        if (!CanAnimate) { ResetVisual(); return; }
        float horizontal = Input.GetAxisRaw("Horizontal");
        bool keyboardHeld = pedal == PedalType.Gas ? horizontal > 0.5f : horizontal < -0.5f;
        bool pressed = heldPointers.Count > 0 || keyboardHeld;
        ApplySprite(pressed);
        float blend = 1f - Mathf.Exp(-animationSpeed * Time.unscaledDeltaTime);
        if (scaleChildArtwork)
        {
            Vector3 target = normalScale * (pressed ? pressedScale : 1f);
            Vector3 scale = Vector3.Lerp(pedalVisual.localScale, target, blend);
            if ((scale - target).sqrMagnitude < 0.000001f) scale = target;
            if (pedalVisual.localScale != scale) pedalVisual.localScale = scale;
        }
        Color tint = pressed ? normalTint * pressedTint : normalTint;
        Color colour = Color.Lerp(pedalImage.color, tint, blend);
        if (Mathf.Abs(colour.r-tint.r)+Mathf.Abs(colour.g-tint.g)+Mathf.Abs(colour.b-tint.b) < 0.001f) colour = tint;
        if (pedalImage.color != colour) pedalImage.color = colour;
    }

    private void ApplySprite(bool pressed)
    {
        if (pedalImage == null || button == null || button.transition != Selectable.Transition.SpriteSwap) return;
        Sprite artwork = null;
        if (!button.IsInteractable()) artwork = button.spriteState.disabledSprite;
        else if (pressed) artwork = button.spriteState.pressedSprite;
        // Null clears Image.overrideSprite and restores the original normal sprite.
        // Compare the effective image to avoid marking unchanged artwork dirty.
        Sprite effective = artwork != null ? artwork : pedalImage.sprite;
        if (pedalImage.overrideSprite != effective) pedalImage.overrideSprite = artwork;
    }

    private void ResetVisual()
    {
        heldPointers.Clear();
        ApplySprite(false);
        if (scaleChildArtwork && pedalVisual != null) pedalVisual.localScale = normalScale;
        if (pedalImage != null && pedalImage.color != normalTint) pedalImage.color = normalTint;
    }
    private void OnDisable() => ResetVisual();
    private void OnApplicationFocus(bool focused) { focusLost = !focused; if (!focused) ResetVisual(); }
    private void OnApplicationPause(bool isPaused) { paused = isPaused; if (isPaused) ResetVisual(); }
}
