using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Small UI motion helper (VRCTween only, nothing runs between events), local:
    /// <list type="bullet">
    /// <item><c>_Bounce</c>: press feedback, scale 0.96 → 1 (0.15 s OutCubic) + optional click sound. Wire a Button's onClick to it.</item>
    /// <item>Appear: when <see cref="appearOnEnable"/> is set and a CanvasGroup is assigned, fades in (and rises) every time it is enabled.</item>
    /// </list>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSUIAnim : UdonSharpBehaviour
    {
        [Header("Press")]
        [Tooltip("Optional click sound (local), moved to this button and played once.")]
        [SerializeField] private AudioSource click;

        [Header("Appear")]
        [SerializeField] private bool appearOnEnable;
        [SerializeField] private CanvasGroup group;
        [Tooltip("Start this far below the resting place (canvas units). 0 = fade only.")]
        [SerializeField] private float rise = 16f;
        [SerializeField] private float duration = 0.2f;

        private Vector3 size, home;
        private bool sized, homed;
        private VRCTweenHandle scaleTween, fadeTween, moveTween;

        public void _Bounce()
        {
            if (!sized) { size = transform.localScale; sized = true; }
            scaleTween.Kill();
            transform.localScale = size * 0.96f;
            scaleTween = transform.TweenScale(size, 0.15f, VRCTweenEase.OutCubic);
            if (click == null) return;
            click.transform.position = transform.position;
            click.PlayOneShot(click.clip);
        }

        /// <summary>Fade/rise in now (optionally after a delay), e.g. for staggered grid cells.</summary>
        public void Appear(float delay)
        {
            if (group == null) return;
            if (!homed) { home = transform.localPosition; homed = true; }
            fadeTween.Kill();
            moveTween.Kill();
            group.alpha = 0f;
            fadeTween = group.TweenFade(1f, duration, VRCTweenEase.OutCubic).SetDelay(delay);
            if (rise == 0f) return;
            transform.localPosition = home + Vector3.down * rise;
            moveTween = transform.TweenLocalPosition(home, duration, VRCTweenEase.OutCubic).SetDelay(delay);
        }

        private void OnEnable()
        {
            if (appearOnEnable) Appear(0f);
        }

        private void OnDisable()
        {
            scaleTween.Kill();
            fadeTween.Kill();
            moveTween.Kill();
            if (sized) transform.localScale = size;
            if (group != null) group.alpha = 1f;
            if (homed) transform.localPosition = home;
        }
    }
}
