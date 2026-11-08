using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// One physical record in its album sleeve (pickup). Only the sleeve is shown; the vinyl itself appears on a
    /// turntable platter while it plays (the sleeve then waits at the turntable's eject point). Lives on the same GameObject as a VRCPickup + VRCObjectSync, so it does not
    /// sync variables itself (Manual sync cannot share a GameObject with VRCObjectSync). Which album a pool record
    /// shows is synced by <see cref="MVSDiscManager"/>; position is synced by VRCObjectSync.
    ///
    /// Two kinds:
    /// <list type="bullet">
    /// <item><b>Pool record</b> (<see cref="fixedAlbumId"/> empty): spawned from the album menu, album changes per spawn.</item>
    /// <item><b>Display record</b> (<see cref="fixedAlbumId"/> set): always in the scene, returns to its shelf spot when idle.</item>
    /// </list>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSDisc : UdonSharpBehaviour
    {
        [Tooltip("Display records only: the library album id this record always shows. Leave empty for pool records.")]
        public string fixedAlbumId = "";

        [Header("Visual")]
        [Tooltip("Renderer of the sleeve (MeijiVinylSystem/Record Sleeve shader).")]
        [SerializeField] private Renderer coverRenderer;
        [Tooltip("Pool sleeves: show the album's front/back art from the library atlases. " +
                 "Turn off for display sleeves that keep their own material.")]
        [SerializeField] private bool applyLibraryArt = true;
        [SerializeField] private string coverProperty = "_CoverTex";
        [SerializeField] private string coverRectProperty = "_CoverRect";
        [SerializeField] private string backProperty = "_BackTex";
        [SerializeField] private string backRectProperty = "_BackRect";

        [Header("Components (auto-filled when empty)")]
        [SerializeField] private VRCPickup pickup;
        [SerializeField] private VRCObjectSync objectSync;
        [SerializeField] private Collider discCollider;

        // Set by the manager at runtime.
        [System.NonSerialized] public MVSDiscManager manager;
        [System.NonSerialized] public int discId = -1;

        private int album = -1;
        private bool locked;
        private bool parked;
        private MaterialPropertyBlock block;

        private void Start() { EnsureComponents(); }

        public void EnsureComponents()
        {
            if (!Utilities.IsValid(pickup)) pickup = (VRCPickup)GetComponent(typeof(VRCPickup));
            if (!Utilities.IsValid(objectSync)) objectSync = (VRCObjectSync)GetComponent(typeof(VRCObjectSync));
            if (!Utilities.IsValid(discCollider)) discCollider = GetComponent<Collider>();
        }

        // ---- State ------------------------------------------------------------------------------

        public bool IsDisplayRecord() { return !string.IsNullOrEmpty(fixedAlbumId); }
        public int GetAlbum() { return album; }
        public int GetDiscId() { return discId; }
        public bool IsLocked() { return locked; }
        public bool IsParked() { return parked; }
        public bool IsHeld() { return Utilities.IsValid(pickup) && pickup.IsHeld; }

        public VRCPlayerApi GetHolder() { return Utilities.IsValid(pickup) ? pickup.currentPlayer : null; }

        /// <summary>Shows the album's cover from the library cover atlas (blank label when none). Local only.</summary>
        public void SetAlbum(int albumIndex, MVSLibrary library)
        {
            album = albumIndex;
            if (!applyLibraryArt || !Utilities.IsValid(coverRenderer)) return;
            if (block == null) block = new MaterialPropertyBlock();
            coverRenderer.GetPropertyBlock(block);
            Texture2D page = Utilities.IsValid(library) ? library.GetCoverPage(albumIndex) : null;
            if (Utilities.IsValid(page))
            {
                block.SetTexture(coverProperty, page);
                block.SetVector(coverRectProperty, library.GetCoverRect(albumIndex));
            }
            else block.SetVector(coverRectProperty, Vector4.zero);
            Texture2D back = Utilities.IsValid(library) ? library.GetBackPage(albumIndex) : null;
            if (Utilities.IsValid(back))
            {
                block.SetTexture(backProperty, back);
                block.SetVector(backRectProperty, library.GetBackRect(albumIndex));
            }
            else block.SetVector(backRectProperty, Vector4.zero);
            coverRenderer.SetPropertyBlock(block);
        }

        // ---- Movement (caller must own the object; these take ownership) -----------------------

        /// <summary>Moves the record to a transform (e.g. the menu spawn point). Takes ownership.</summary>
        public void TeleportTo(Transform target)
        {
            if (!Utilities.IsValid(target)) return;
            EnsureComponents();
            TakeOwnership();
            if (IsHeld() && Utilities.IsValid(pickup)) pickup.Drop();
            if (Utilities.IsValid(objectSync)) objectSync.TeleportTo(target);
            else transform.SetPositionAndRotation(target.position, target.rotation);
        }

        /// <summary>
        /// After a teleport: glide out along the record's face normal (local up) by <paramref name="distance"/> m,
        /// like sliding out of a sleeve. Owner only (VRCObjectSync carries the motion to everyone).
        /// </summary>
        public void PopOut(float distance, float duration)
        {
            if (distance == 0f || !Networking.IsOwner(gameObject)) return;
            popTween.Kill();
            Vector3 target = transform.position + transform.up * distance;
            popTween = transform.TweenPosition(target, duration, VRCTweenEase.OutBack);
        }

        private VRCTweenHandle popTween;

        /// <summary>Display records: back to the original shelf spot. Takes ownership.</summary>
        public void ReturnHome()
        {
            EnsureComponents();
            TakeOwnership();
            if (IsHeld() && Utilities.IsValid(pickup)) pickup.Drop();
            if (Utilities.IsValid(objectSync)) objectSync.Respawn();
        }

        /// <summary>
        /// Locks the record (not pickupable) e.g. while it sits on a platter. Local; every client calls it
        /// from the player/turntable events.
        /// </summary>
        public void SetLocked(bool value)
        {
            locked = value;
            EnsureComponents();
            if (Utilities.IsValid(pickup))
            {
                if (value && pickup.IsHeld && Utilities.IsValid(pickup.currentPlayer) && pickup.currentPlayer.isLocal) pickup.Drop();
                pickup.pickupable = !value;
            }
        }

        /// <summary>
        /// The record is out of this sleeve, playing on a turntable: the empty sleeve waits next to the deck
        /// and can't be picked up until the record comes back (local, every client calls it from turntable events).
        /// </summary>
        public void SetParked(bool value)
        {
            parked = value;
            EnsureComponents();
            SetLocked(value);
        }

        public Renderer GetRenderer() { return coverRenderer; }

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        // ---- Pickup events ------------------------------------------------------------------------

        public override void OnPickup()
        {
            popTween.Kill();
            if (Utilities.IsValid(manager)) manager._NotifyDiscTouched(discId);
        }

        public override void OnDrop()
        {
            if (Utilities.IsValid(manager)) manager._NotifyDiscTouched(discId);
        }
    }
}
