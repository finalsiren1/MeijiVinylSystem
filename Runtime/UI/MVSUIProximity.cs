using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Shows a world-space UI only when the local player is near: pops in (fade + slight scale) inside
    /// <see cref="showDistance"/>, fades out beyond <see cref="hideDistance"/>. While hidden the Canvas stops
    /// rendering and its collider is off (no laser hits), but the GameObject stays active so UdonBehaviours keep
    /// receiving events. Checks distance 4 times a second (local only).
    /// Put it on the Canvas root together with a CanvasGroup.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSUIProximity : UdonSharpBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Canvas canvas;
        [Tooltip("Collider used by VRChat for UI raycasts (the canvas BoxCollider).")]
        [SerializeField] private Collider uiCollider;
        [Tooltip("Distance is measured from this point (default: this transform).")]
        [SerializeField] private Transform center;
        [SerializeField, Min(0.1f)] private float showDistance = 2.5f;
        [SerializeField, Min(0.1f)] private float hideDistance = 3f;
        [SerializeField, Min(0.01f)] private float showTime = 0.25f;
        [SerializeField, Min(0.01f)] private float hideTime = 0.2f;
        [Tooltip("Scale it pops in from (1 = no scale motion).")]
        [SerializeField, Range(0.5f, 1f)] private float popScale = 0.94f;
        [SerializeField, Min(0.05f)] private float checkInterval = 0.25f;

        private bool shown;
        private Vector3 baseScale;
        private VRCTweenHandle fadeTween, scaleTween, hideTimer;

        private void Start()
        {
            if (!Utilities.IsValid(center)) center = transform;
            baseScale = transform.localScale;
            SetVisible(false, true);
            SendCustomEventDelayedSeconds(nameof(_Check), 0.5f);
        }

        public void _Check()
        {
            SendCustomEventDelayedSeconds(nameof(_Check), checkInterval);
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            float d = Vector3.Distance(local.GetPosition(), center.position);
            if (!shown && d <= showDistance) SetVisible(true, false);
            else if (shown && d > hideDistance) SetVisible(false, false);
        }

        public bool IsShown() { return shown; }

        private void SetVisible(bool value, bool instant)
        {
            shown = value;
            fadeTween.Kill();
            scaleTween.Kill();
            hideTimer.Kill();
            if (value)
            {
                if (Utilities.IsValid(canvas)) canvas.enabled = true;
                if (Utilities.IsValid(uiCollider)) uiCollider.enabled = true;
                if (Utilities.IsValid(group)) { group.interactable = true; group.blocksRaycasts = true; }
                if (instant)
                {
                    if (Utilities.IsValid(group)) group.alpha = 1f;
                    transform.localScale = baseScale;
                    return;
                }
                if (Utilities.IsValid(group)) fadeTween = group.TweenFade(1f, showTime, VRCTweenEase.OutCubic);
                transform.localScale = baseScale * popScale;
                scaleTween = transform.TweenScale(baseScale, showTime, VRCTweenEase.OutBack);
            }
            else
            {
                if (Utilities.IsValid(group)) { group.interactable = false; group.blocksRaycasts = false; }
                if (Utilities.IsValid(uiCollider)) uiCollider.enabled = false;
                if (instant)
                {
                    if (Utilities.IsValid(group)) group.alpha = 0f;
                    if (Utilities.IsValid(canvas)) canvas.enabled = false;
                    return;
                }
                if (Utilities.IsValid(group)) fadeTween = group.TweenFade(0f, hideTime, VRCTweenEase.InCubic);
                scaleTween = transform.TweenScale(baseScale * popScale, hideTime, VRCTweenEase.InCubic);
                hideTimer = VRCTween.DelayedCall(this, nameof(_HideCanvas), hideTime);
            }
        }

        public void _HideCanvas()
        {
            if (shown) return;
            if (Utilities.IsValid(canvas)) canvas.enabled = false;
            transform.localScale = baseScale;
        }
    }
}
