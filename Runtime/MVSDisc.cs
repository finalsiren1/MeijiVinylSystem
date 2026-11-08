using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// One physical record (pickup). Lives on the same GameObject as a VRCPickup + VRCObjectSync, so it does not
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
        [Tooltip("Renderer whose material has the cover texture property (MeijiVinylSystem/Vinyl Record shader).")]
        [SerializeField] private Renderer coverRenderer;
        [SerializeField] private string coverProperty = "_CoverTex";
        [SerializeField] private string coverRectProperty = "_CoverRect";

        [Header("Components (auto-filled when empty)")]
        [SerializeField] private VRCPickup pickup;
        [SerializeField] private VRCObjectSync objectSync;
        [SerializeField] private Collider discCollider;

        // Set by the manager at runtime.
        [System.NonSerialized] public MVSDiscManager manager;
        [System.NonSerialized] public int discId = -1;

        private int album = -1;
        private bool locked;
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
        public bool IsHeld() { return Utilities.IsValid(pickup) && pickup.IsHeld; }

        public VRCPlayerApi GetHolder() { return Utilities.IsValid(pickup) ? pickup.currentPlayer : null; }

        /// <summary>Shows the album's cover from the library cover atlas (blank label when none). Local only.</summary>
        public void SetAlbum(int albumIndex, MVSLibrary library)
        {
            album = albumIndex;
            if (!Utilities.IsValid(coverRenderer)) return;
            if (block == null) block = new MaterialPropertyBlock();
            coverRenderer.GetPropertyBlock(block);
            Texture2D page = Utilities.IsValid(library) ? library.GetCoverPage(albumIndex) : null;
            if (Utilities.IsValid(page))
            {
                block.SetTexture(coverProperty, page);
                block.SetVector(coverRectProperty, library.GetCoverRect(albumIndex));
            }
            else block.SetVector(coverRectProperty, Vector4.zero);
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

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        // ---- Pickup events ------------------------------------------------------------------------

        public override void OnPickup()
        {
            if (Utilities.IsValid(manager)) manager._NotifyDiscTouched(discId);
        }

        public override void OnDrop()
        {
            if (Utilities.IsValid(manager)) manager._NotifyDiscTouched(discId);
        }
    }
}
