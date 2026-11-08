using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>Click the booth headphones to put them on (they then follow your head). Needs a collider.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSHeadphones : UdonSharpBehaviour
    {
        [SerializeField] private MVSBooth booth;
        [SerializeField] private string putOnText = "Put on headphones";
        [SerializeField] private string takenText = "In use";

        private void Start()
        {
            InteractionText = putOnText;
            if (Utilities.IsValid(booth)) booth._AddListener(this);
        }

        public override void Interact()
        {
            if (Utilities.IsValid(booth)) booth._PutOn();
        }

        public void _MVSBoothChanged()
        {
            if (!Utilities.IsValid(booth)) return;
            bool free = !booth.IsWorn();
            InteractionText = free ? putOnText : takenText;
            DisableInteractive = !free;
        }
    }
}
