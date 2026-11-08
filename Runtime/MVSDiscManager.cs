using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Owns every <see cref="MVSDisc"/> in the scene: the spawnable record pool and the display records.
    ///
    /// Synced (Manual, owner writes): for each pool slot, which album it shows (-1 = put away) and when it was spawned.
    /// Reset All affects pool records only; display records are separate (idle return / ResetDisplayRecords).
    /// Housekeeping (owner only, 1 Hz): records left alone for <see cref="idleReturnSeconds"/> go back
    /// (pool → put away, display → shelf spot). Records that are held or sitting in a player are never touched.
    ///
    /// Spawn rules: while more than <see cref="softLimit"/> pool records are out, spawning puts the oldest idle one
    /// away first. If every slot is out and none is idle, spawning is refused (<see cref="IsAtLimit"/>) until a Reset.
    ///
    /// Listener event: <c>_MVSDiscsChanged</c> (pool count / limit changed).
    /// Disc ids: pool records 0..pool-1, display records pool..pool+display-1.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [DisallowMultipleComponent]
    public class MVSDiscManager : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private MVSLibrary library;
        [Tooltip("Record players (global and local booths). Records on their platters are 'in use'.")]
        [SerializeField] private MVSPlayer[] players = new MVSPlayer[0];
        [Tooltip("Where records appear when spawned from the album menu.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("After spawning, the record glides this far out of the spawn point along its face (m). 0 = no motion.")]
        [SerializeField] private float spawnPopDistance = 0.12f;
        [SerializeField, Min(0.05f)] private float spawnPopTime = 0.45f;

        [Header("Records")]
        [SerializeField] private MVSDisc[] poolDiscs = new MVSDisc[0];
        [SerializeField] private MVSDisc[] displayDiscs = new MVSDisc[0];

        [Header("Rules")]
        [Tooltip("Above this many pool records out, spawning recycles the oldest idle one.")]
        [SerializeField, Min(1)] private int softLimit = 10;
        [Tooltip("Seconds a record can lie around untouched before it returns.")]
        [SerializeField, Min(5)] private float idleReturnSeconds = 60f;
        [Tooltip("Display records count as 'away' when farther than this from their shelf spot (m).")]
        [SerializeField, Min(0.001f)] private float displayHomeTolerance = 0.05f;

        [Header("Listeners")]
        [SerializeField] private UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[0];

        public const int SPAWN_OK = 0, SPAWN_LIMIT = 1, SPAWN_INVALID = 2, SPAWN_ALREADY_OUT = 3;

        [UdonSynced] private int[] poolAlbum = new int[0];
        [UdonSynced] private int[] poolSpawnTime = new int[0];

        private float[] idleTime = new float[0];        // local, owner uses it
        private Vector3[] displayHome = new Vector3[0];
        private int lastSpawnResult = SPAWN_OK;
        private int activeCount;
        private bool initialized;

        private void Start()
        {
            Initialize();
            SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
        }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            int n = poolDiscs.Length;
            if (poolAlbum == null || poolAlbum.Length != n)
            {
                poolAlbum = new int[n];
                poolSpawnTime = new int[n];
                for (int i = 0; i < n; i++) poolAlbum[i] = -1;
            }
            idleTime = new float[n + displayDiscs.Length];
            displayHome = new Vector3[displayDiscs.Length];

            for (int i = 0; i < n; i++)
            {
                MVSDisc d = poolDiscs[i];
                if (!Utilities.IsValid(d)) continue;
                d.manager = this;
                d.discId = i;
            }
            for (int i = 0; i < displayDiscs.Length; i++)
            {
                MVSDisc d = displayDiscs[i];
                if (!Utilities.IsValid(d)) continue;
                d.manager = this;
                d.discId = n + i;
                displayHome[i] = d.transform.position;
                int album = Utilities.IsValid(library) ? library.FindAlbum(d.fixedAlbumId) : -1;
                d.SetAlbum(album, library);
            }
            ApplyPool();
        }

        // =========================================================================================
        // Read API
        // =========================================================================================

        public MVSLibrary GetLibrary() { return library; }
        public int GetPoolSize() { return poolDiscs.Length; }
        public int GetActiveCount() { return activeCount; }
        public int GetSoftLimit() { return softLimit; }
        public int GetLastSpawnResult() { return lastSpawnResult; }
        public int GetDiscCount() { return poolDiscs.Length + displayDiscs.Length; }

        /// <summary>Every pool slot is out and none can be recycled: the menu should ask for a Reset.</summary>
        public bool IsAtLimit()
        {
            if (activeCount < poolDiscs.Length) return false;
            return FindRecyclable() < 0;
        }

        public MVSDisc GetDisc(int discId)
        {
            if (discId < 0) return null;
            if (discId < poolDiscs.Length) return poolDiscs[discId];
            int d = discId - poolDiscs.Length;
            return d < displayDiscs.Length ? displayDiscs[d] : null;
        }

        /// <summary>Album shown by a record id, -1 if none / put away.</summary>
        public int GetDiscAlbum(int discId)
        {
            MVSDisc disc = GetDisc(discId);
            return Utilities.IsValid(disc) ? disc.GetAlbum() : -1;
        }

        /// <summary>The record player whose platter holds this record, or null.</summary>
        public MVSPlayer GetPlayerUsing(int discId)
        {
            if (discId < 0) return null;
            for (int i = 0; i < players.Length; i++)
            {
                MVSPlayer p = players[i];
                if (Utilities.IsValid(p) && p.HasRecord() && p.GetDisc() == discId) return p;
            }
            return null;
        }

        public bool IsDiscInUse(int discId) { return Utilities.IsValid(GetPlayerUsing(discId)); }

        /// <summary>Pool slot currently showing this album, or -1.</summary>
        public int FindPoolDiscWithAlbum(int album)
        {
            for (int i = 0; i < poolAlbum.Length; i++) if (poolAlbum[i] == album) return i;
            return -1;
        }

        // =========================================================================================
        // Actions
        // =========================================================================================

        /// <summary>
        /// Takes a record of this album out of the pool and puts it at the spawn point. Returns a SPAWN_* code.
        /// If that album is already out, the existing record is brought to the spawn point instead (SPAWN_ALREADY_OUT)
        /// unless it is held or in use.
        /// </summary>
        public int Spawn(int album)
        {
            Initialize();
            if (!Utilities.IsValid(library) || !library.IsValidAlbum(album)) return Result(SPAWN_INVALID);

            int existing = FindPoolDiscWithAlbum(album);
            if (existing >= 0)
            {
                MVSDisc d = poolDiscs[existing];
                if (!d.IsHeld() && !IsDiscInUse(existing)) { d.TeleportTo(spawnPoint); d.PopOut(spawnPopDistance, spawnPopTime); }
                idleTime[existing] = 0f;
                return Result(SPAWN_ALREADY_OUT);
            }

            TakeOwnership();
            if (CountActive() >= softLimit)
            {
                int recycle = FindRecyclable();
                if (recycle >= 0) poolAlbum[recycle] = -1;
            }
            int slot = -1;
            for (int i = 0; i < poolAlbum.Length; i++)
                if (poolAlbum[i] < 0 && Utilities.IsValid(poolDiscs[i])) { slot = i; break; }
            if (slot < 0)
            {
                ApplyPool();
                return Result(SPAWN_LIMIT);
            }

            poolAlbum[slot] = album;
            poolSpawnTime[slot] = Networking.GetServerTimeInMilliseconds();
            idleTime[slot] = 0f;
            RequestSerialization();
            ApplyPool();
            poolDiscs[slot].TeleportTo(spawnPoint); // active now locally; ObjectSync carries the position
            poolDiscs[slot].PopOut(spawnPopDistance, spawnPopTime);
            return Result(SPAWN_OK);
        }

        /// <summary>
        /// Puts every POOL record away (menu records), except records sitting in a player.
        /// Display records are not affected; they return on their own when idle (or use <see cref="ResetDisplayRecords"/>).
        /// </summary>
        public void ResetAll()
        {
            Initialize();
            TakeOwnership();
            for (int i = 0; i < poolAlbum.Length; i++)
            {
                if (poolAlbum[i] < 0 || IsDiscInUse(i)) continue;
                poolAlbum[i] = -1; // held ones too: deactivating the object releases it for the holder
            }
            RequestSerialization();
            ApplyPool();
            lastSpawnResult = SPAWN_OK;
            Broadcast("_MVSDiscsChanged");
        }

        public void _ResetAll() { ResetAll(); }

        /// <summary>Separate from <see cref="ResetAll"/>: sends every display record that is away (and not in use) home.</summary>
        public void ResetDisplayRecords()
        {
            Initialize();
            for (int i = 0; i < displayDiscs.Length; i++)
            {
                int id = poolDiscs.Length + i;
                if (Utilities.IsValid(displayDiscs[i]) && !IsBusy(id) && IsAway(i)) displayDiscs[i].ReturnHome();
            }
        }

        public void _ResetDisplayRecords() { ResetDisplayRecords(); }

        /// <summary>Puts one pool record away (e.g. after it was ejected). Display records go home instead.</summary>
        public void ReturnDisc(int discId)
        {
            Initialize();
            if (discId < 0) return;
            if (discId < poolDiscs.Length)
            {
                if (poolAlbum[discId] < 0) return;
                TakeOwnership();
                poolAlbum[discId] = -1;
                RequestSerialization();
                ApplyPool();
                return;
            }
            MVSDisc d = GetDisc(discId);
            if (Utilities.IsValid(d)) d.ReturnHome();
        }

        /// <summary>Called by a record on pickup/drop (local) to restart its idle timer.</summary>
        public void _NotifyDiscTouched(int discId)
        {
            if (discId >= 0 && discId < idleTime.Length) idleTime[discId] = 0f;
        }

        // =========================================================================================
        // Housekeeping (owner)
        // =========================================================================================

        public void _Tick()
        {
            SendCustomEventDelayedSeconds(nameof(_Tick), 1f);
            if (!initialized || !Networking.IsOwner(gameObject)) return;

            bool changed = false;
            for (int i = 0; i < poolAlbum.Length; i++)
            {
                if (poolAlbum[i] < 0) { idleTime[i] = 0f; continue; }
                if (IsBusy(i)) { idleTime[i] = 0f; continue; }
                idleTime[i] += 1f;
                if (idleTime[i] >= idleReturnSeconds)
                {
                    poolAlbum[i] = -1;
                    idleTime[i] = 0f;
                    changed = true;
                }
            }
            if (changed)
            {
                RequestSerialization();
                ApplyPool();
            }

            for (int i = 0; i < displayDiscs.Length; i++)
            {
                int id = poolDiscs.Length + i;
                if (!Utilities.IsValid(displayDiscs[i]) || IsBusy(id) || !IsAway(i)) { idleTime[id] = 0f; continue; }
                idleTime[id] += 1f;
                if (idleTime[id] >= idleReturnSeconds)
                {
                    idleTime[id] = 0f;
                    displayDiscs[i].ReturnHome();
                }
            }
        }

        private bool IsBusy(int discId)
        {
            MVSDisc d = GetDisc(discId);
            if (!Utilities.IsValid(d)) return false;
            return d.IsHeld() || IsDiscInUse(discId);
        }

        private bool IsAway(int displayIndex)
        {
            MVSDisc d = displayDiscs[displayIndex];
            return Vector3.Distance(d.transform.position, displayHome[displayIndex]) > displayHomeTolerance;
        }

        /// <summary>Oldest pool record that is out, not held and not in a player; -1 if none.</summary>
        private int FindRecyclable()
        {
            int best = -1;
            int bestTime = 0;
            for (int i = 0; i < poolAlbum.Length; i++)
            {
                if (poolAlbum[i] < 0 || IsBusy(i)) continue;
                if (best < 0 || poolSpawnTime[i] - bestTime < 0) { best = i; bestTime = poolSpawnTime[i]; }
            }
            return best;
        }

        // =========================================================================================
        // Sync / apply
        // =========================================================================================

        public override void OnDeserialization()
        {
            Initialize();
            ApplyPool();
        }

        private void ApplyPool()
        {
            int before = activeCount;
            activeCount = 0;
            for (int i = 0; i < poolDiscs.Length && i < poolAlbum.Length; i++)
            {
                MVSDisc d = poolDiscs[i];
                if (!Utilities.IsValid(d)) continue;
                int album = poolAlbum[i];
                bool active = album >= 0;
                if (active)
                {
                    activeCount++;
                    if (d.GetAlbum() != album)
                        d.SetAlbum(album, library);
                }
                if (d.gameObject.activeSelf != active) d.gameObject.SetActive(active);
            }
            if (activeCount < poolDiscs.Length && lastSpawnResult == SPAWN_LIMIT) lastSpawnResult = SPAWN_OK;
            if (before != activeCount) Broadcast("_MVSDiscsChanged");
        }

        private int CountActive()
        {
            int c = 0;
            for (int i = 0; i < poolAlbum.Length; i++) if (poolAlbum[i] >= 0) c++;
            return c;
        }

        private int Result(int code)
        {
            lastSpawnResult = code;
            Broadcast("_MVSDiscsChanged");
            return code;
        }

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        public void _AddListener(UdonSharpBehaviour listener)
        {
            if (!Utilities.IsValid(listener)) return;
            for (int i = 0; i < listeners.Length; i++) if (listeners[i] == listener) return;
            var grown = new UdonSharpBehaviour[listeners.Length + 1];
            for (int i = 0; i < listeners.Length; i++) grown[i] = listeners[i];
            grown[listeners.Length] = listener;
            listeners = grown;
        }

        private void Broadcast(string eventName)
        {
            for (int i = 0; i < listeners.Length; i++)
                if (Utilities.IsValid(listeners[i])) listeners[i].SendCustomEvent(eventName);
        }
    }
}
