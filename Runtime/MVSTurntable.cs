using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// The physical turntable: insert trigger, platter spin, tonearm motion, record on the platter, sounds.
    /// Purely local presentation driven by <see cref="MVSPlayer"/> events, so every client animates the same
    /// sequence; nothing is synced here. Works with any model: assign the platter and tonearm transforms and
    /// tune the angles/axes (tonearm angles are relative to the tonearm's pose in the scene).
    ///
    /// Insert: hold a record sleeve and touch this GameObject's trigger collider. The empty sleeve is laid at
    /// <see cref="ejectPoint"/> (not pickupable) while the vinyl (<see cref="recordVisual"/>) drops onto the platter.
    /// Sequence: record drops → platter spins up → tonearm swings over and lowers (needle-drop sound) → music
    /// (the player's Start Delay should be ≥ dropTime + armMoveTime + lowerTime). Eject reverses it and the
    /// real record reappears at <see cref="ejectPoint"/>, free to pick up (it returns home after the idle time).
    ///
    /// Optional Animator parameters (if an Animator is assigned): bool "HasRecord", bool "Spinning".
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSTurntable : UdonSharpBehaviour
    {
        [Header("System")]
        [SerializeField] private MVSPlayer player;
        [SerializeField] private MVSDiscManager discs;

        [Header("Platter")]
        [SerializeField] private Transform platter;
        [Tooltip("Platter spin axis in the platter's local space.")]
        [SerializeField] private Vector3 platterAxis = Vector3.up;
        [SerializeField] private float rpm = 33.333f;
        [SerializeField, Min(0.01f)] private float spinUpTime = 0.8f;
        [SerializeField, Min(0.01f)] private float spinDownTime = 1.6f;

        [Header("Record on the platter")]
        [Tooltip("Renderer (child of the platter) that shows the playing record. Uses the Vinyl Record shader.")]
        [SerializeField] private Renderer recordVisual;
        [Tooltip("Height (m, platter local) the record drops from.")]
        [SerializeField] private float dropHeight = 0.12f;
        [SerializeField, Min(0.01f)] private float dropTime = 0.6f;
        [Tooltip("Where the real record is parked while playing and where it pops out on eject (keep it outside the trigger).")]
        [SerializeField] private Transform ejectPoint;

        [Header("Tonearm")]
        [SerializeField] private Transform tonearm;
        [Tooltip("Swing (yaw) axis in the tonearm's local space.")]
        [SerializeField] private Vector3 tonearmAxis = Vector3.up;
        [Tooltip("Lift (pitch) axis in the tonearm's local space.")]
        [SerializeField] private Vector3 liftAxis = Vector3.right;
        [Tooltip("Swing angle at rest (off the record), degrees from the scene pose.")]
        [SerializeField] private float restAngle = 22f;
        [Tooltip("Swing angle at the first groove.")]
        [SerializeField] private float startAngle = 0f;
        [Tooltip("Swing angle at the end of the side (run-out).")]
        [SerializeField] private float endAngle = -18f;
        [Tooltip("Lift angle while raised, degrees.")]
        [SerializeField] private float liftAngle = 4f;
        [SerializeField, Min(0.01f)] private float armMoveTime = 0.9f;
        [SerializeField, Min(0.01f)] private float lowerTime = 0.35f;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource sfx;
        [SerializeField] private AudioClip needleDropClip;
        [SerializeField] private AudioClip liftClip;
        [SerializeField] private AudioClip denyClip;

        [Header("Animator (optional)")]
        [SerializeField] private Animator animator;

        [Header("Shader properties")]
        [SerializeField] private string coverProperty = "_CoverTex";
        [SerializeField] private string coverRectProperty = "_CoverRect";
        [SerializeField] private string labelProperty = "_LabelTex";
        [SerializeField] private string labelRectProperty = "_LabelRect";

        private const int SEQ_NONE = 0, SEQ_INSERT = 1, SEQ_EJECT = 2;

        private Quaternion platterBase, armBase;
        private Vector3 recordBasePos;
        private float platterAngle, platterSpeed; // speed in deg/s
        private int seq = SEQ_NONE;
        private float seqTime;
        private bool needleSoundPlayed;
        private float armYaw, armLift;
        private float ejectFromYaw;
        private int shownAlbum = -1;
        private int parkedDisc = -1;
        private bool hasRecordVisual;
        private MaterialPropertyBlock block;

        private void Start()
        {
            if (Utilities.IsValid(platter)) platterBase = platter.localRotation;
            if (Utilities.IsValid(tonearm)) armBase = tonearm.localRotation;
            if (Utilities.IsValid(recordVisual)) recordBasePos = recordVisual.transform.localPosition;
            armYaw = restAngle;
            armLift = liftAngle;
            if (Utilities.IsValid(player)) player._AddListener(this);
            ApplyRecordVisual(false, 1f);
            ApplyArm();
            Snap();
        }

        // =========================================================================================
        // Insert trigger
        // =========================================================================================

        private void OnTriggerEnter(Collider other)
        {
            if (!Utilities.IsValid(other) || !Utilities.IsValid(player)) return;
            MVSDisc disc = other.GetComponent<MVSDisc>();
            if (!Utilities.IsValid(disc) || disc.IsParked()) return;
            VRCPlayerApi holder = disc.GetHolder();
            if (!Utilities.IsValid(holder) || !holder.isLocal) return; // only the hand holding it inserts
            if (disc.GetAlbum() < 0 || !player.CanInsert())
            {
                PlayClip(denyClip);
                return;
            }
            int album = disc.GetAlbum();
            int id = disc.GetDiscId();
            disc.TeleportTo(ejectPoint); // drops it and lays the empty sleeve next to the deck
            player.InsertRecord(album, id);
        }

        // =========================================================================================
        // MVSPlayer events
        // =========================================================================================

        public void _MVSRecordChanged()
        {
            bool hasRecord = player.HasRecord();
            if (player.IsInstantApply()) { Snap(); return; }
            if (hasRecord)
            {
                ParkDisc(player.GetDisc());
                ShowAlbum(player.GetAlbum());
                StartSequence(SEQ_INSERT);
            }
            else
            {
                ejectFromYaw = armYaw;
                StartSequence(SEQ_EJECT);
            }
        }

        public void _MVSStateChanged() { }
        public void _MVSTrackChanged() { }
        public void _MVSLoopChanged() { }
        public void _MVSPlaybackChanged() { }

        // =========================================================================================
        // Animation
        // =========================================================================================

        private void StartSequence(int which)
        {
            seq = which;
            seqTime = 0f;
            needleSoundPlayed = false;
            if (which == SEQ_EJECT) PlayClip(liftClip);
        }

        /// <summary>Jump straight to the state the player is in (late join, instant apply).</summary>
        private void Snap()
        {
            seq = SEQ_NONE;
            bool hasRecord = Utilities.IsValid(player) && player.HasRecord();
            if (hasRecord)
            {
                ParkDisc(player.GetDisc());
                ShowAlbum(player.GetAlbum());
                ApplyRecordVisual(true, 1f);
                armYaw = PlayingYaw();
                armLift = 0f;
            }
            else
            {
                UnparkDisc();
                ApplyRecordVisual(false, 1f);
                armYaw = restAngle;
                armLift = liftAngle;
            }
            platterSpeed = hasRecord && !player.IsPaused() ? rpm * 6f : 0f;
            ApplyArm();
            ApplyAnimator(hasRecord);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool hasRecord = Utilities.IsValid(player) && player.HasRecord();

            if (seq == SEQ_INSERT)
            {
                seqTime += dt;
                float drop = Mathf.Clamp01(seqTime / dropTime);
                ApplyRecordVisual(true, Smooth(drop));
                float armT = Mathf.Clamp01((seqTime - dropTime) / armMoveTime);
                float lowerT = Mathf.Clamp01((seqTime - dropTime - armMoveTime) / lowerTime);
                armYaw = Mathf.Lerp(restAngle, startAngle, Smooth(armT));
                armLift = Mathf.Lerp(liftAngle, 0f, Smooth(lowerT));
                if (lowerT >= 1f && !needleSoundPlayed) { needleSoundPlayed = true; PlayClip(needleDropClip); }
                if (lowerT >= 1f) seq = SEQ_NONE;
                ApplyArm();
                ApplyAnimator(true);
            }
            else if (seq == SEQ_EJECT)
            {
                seqTime += dt;
                float liftT = Mathf.Clamp01(seqTime / lowerTime);
                float armT = Mathf.Clamp01((seqTime - lowerTime) / armMoveTime);
                float upT = Mathf.Clamp01((seqTime - lowerTime - armMoveTime) / dropTime);
                armLift = Mathf.Lerp(0f, liftAngle, Smooth(liftT));
                armYaw = Mathf.Lerp(ejectFromYaw, restAngle, Smooth(armT));
                ApplyRecordVisual(upT < 1f, 1f - Smooth(upT));
                if (upT >= 1f)
                {
                    seq = SEQ_NONE;
                    UnparkDisc(); // the real record pops out at the eject point
                }
                ApplyArm();
                ApplyAnimator(false);
            }
            else if (hasRecord && armLift <= 0f && player.GetState() == MVSPlayer.STATE_PLAYING)
            {
                // Needle follows the song.
                armYaw = PlayingYaw();
                ApplyArm();
            }

            // Platter: spin while a record is on it (after it landed) and not paused.
            bool spin = hasRecord && !(seq == SEQ_INSERT && seqTime < dropTime) && !player.IsPaused();
            if (seq == SEQ_EJECT && seqTime > lowerTime) spin = false;
            float target = spin ? rpm * 6f : 0f; // rpm → deg/s
            float rate = rpm * 6f / (spin ? spinUpTime : spinDownTime);
            platterSpeed = Mathf.MoveTowards(platterSpeed, target, rate * dt);
            if (platterSpeed > 0f && Utilities.IsValid(platter))
            {
                platterAngle = (platterAngle + platterSpeed * dt) % 360f;
                platter.localRotation = platterBase * Quaternion.AngleAxis(platterAngle, platterAxis);
            }
            if (Utilities.IsValid(animator)) animator.SetBool("Spinning", platterSpeed > 0f);
        }

        private float PlayingYaw()
        {
            float duration = player.GetDuration();
            if (duration <= 0f || float.IsInfinity(duration) || float.IsNaN(duration)) return startAngle;
            return Mathf.Lerp(startAngle, endAngle, Mathf.Clamp01(player.GetTime() / duration));
        }

        private static float Smooth(float t) { return t * t * (3f - 2f * t); }

        private void ApplyArm()
        {
            if (!Utilities.IsValid(tonearm)) return;
            tonearm.localRotation = armBase * Quaternion.AngleAxis(armYaw, tonearmAxis) * Quaternion.AngleAxis(-armLift, liftAxis);
        }

        /// <summary>visible + how far down it is (0 = at drop height, 1 = on the platter).</summary>
        private void ApplyRecordVisual(bool visible, float landed)
        {
            if (!Utilities.IsValid(recordVisual)) return;
            if (recordVisual.enabled != visible) recordVisual.enabled = visible;
            hasRecordVisual = visible;
            recordVisual.transform.localPosition = recordBasePos + Vector3.up * dropHeight * (1f - landed);
        }

        private void ApplyAnimator(bool hasRecord)
        {
            if (Utilities.IsValid(animator)) animator.SetBool("HasRecord", hasRecord);
        }

        private void ShowAlbum(int album)
        {
            shownAlbum = album;
            if (!Utilities.IsValid(recordVisual)) return;
            MVSLibrary library = player.GetLibrary();
            if (block == null) block = new MaterialPropertyBlock();
            recordVisual.GetPropertyBlock(block);
            Texture2D page = Utilities.IsValid(library) ? library.GetCoverPage(album) : null;
            if (Utilities.IsValid(page))
            {
                block.SetTexture(coverProperty, page);
                block.SetVector(coverRectProperty, library.GetCoverRect(album));
            }
            else block.SetVector(coverRectProperty, Vector4.zero);
            Texture2D label = Utilities.IsValid(library) ? library.GetLabelPage(album) : null;
            if (Utilities.IsValid(label))
            {
                block.SetTexture(labelProperty, label);
                block.SetVector(labelRectProperty, library.GetLabelRect(album));
            }
            else block.SetVector(labelRectProperty, Vector4.zero);
            recordVisual.SetPropertyBlock(block);
        }

        private void ParkDisc(int discId)
        {
            if (parkedDisc == discId) return;
            UnparkDisc();
            if (!Utilities.IsValid(discs)) return;
            MVSDisc disc = discs.GetDisc(discId);
            if (!Utilities.IsValid(disc)) return;
            disc.SetParked(true);
            parkedDisc = discId;
        }

        private void UnparkDisc()
        {
            if (parkedDisc < 0) return;
            MVSDisc disc = Utilities.IsValid(discs) ? discs.GetDisc(parkedDisc) : null;
            if (Utilities.IsValid(disc)) disc.SetParked(false);
            parkedDisc = -1;
        }

        private void PlayClip(AudioClip clip)
        {
            if (Utilities.IsValid(sfx) && Utilities.IsValid(clip)) sfx.PlayOneShot(clip);
        }
    }
}
