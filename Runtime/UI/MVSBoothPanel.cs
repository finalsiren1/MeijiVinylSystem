using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Booth status + voice mode panel (local UI). Shows who is listening, lets the wearer pick a voice mode
    /// and take the headphones off. Pair it with an <see cref="MVSNowPlaying"/> bound to the booth player.
    /// Buttons (SendCustomEvent): <c>_TakeOff</c>, <c>_PutOn</c>, <c>_VoiceOff</c>, <c>_VoiceFocus</c>, <c>_VoiceMuffle</c>, <c>_VoiceSilent</c>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSBoothPanel : UdonSharpBehaviour
    {
        [SerializeField] private MVSBooth booth;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI hintText;
        [Tooltip("Shown only to the wearer (voice modes, take off).")]
        [SerializeField] private GameObject wearerControls;
        [Tooltip("Shown when the booth is free (put on).")]
        [SerializeField] private GameObject freeControls;
        [Tooltip("Selection marks for Off / Focus / Focus+Muffle / Silent, in that order.")]
        [SerializeField] private GameObject[] modeMarks = new GameObject[0];
        [SerializeField] private TextMeshProUGUI[] modeLabels = new TextMeshProUGUI[0];
        [SerializeField] private Color textColor = new Color(0.941f, 0.949f, 0.965f, 1f);
        [SerializeField] private Color textDimColor = new Color(0.588f, 0.612f, 0.659f, 1f);

        [Header("Texts")]
        [SerializeField] private string freeText = "Listening Booth";
        [SerializeField] private string freeHint = "Put on the headphones to listen alone";
        [SerializeField] private string youText = "You are listening";
        [SerializeField] private string youHint = "Others can't hear this booth";
        [SerializeField] private string otherFormat = "{0} is listening";
        [SerializeField] private string otherHint = "Wait until the headphones are free";

        private void Start()
        {
            if (Utilities.IsValid(booth)) booth._AddListener(this);
            Refresh();
        }

        public void _MVSBoothChanged() { Refresh(); }

        public void _PutOn() { if (Utilities.IsValid(booth)) booth._PutOn(); }
        public void _TakeOff() { if (Utilities.IsValid(booth)) booth._TakeOff(); }
        public void _VoiceOff() { if (Utilities.IsValid(booth)) booth.SetVoiceMode(MVSBooth.VOICE_OFF); }
        public void _VoiceFocus() { if (Utilities.IsValid(booth)) booth.SetVoiceMode(MVSBooth.VOICE_FOCUS); }
        public void _VoiceMuffle() { if (Utilities.IsValid(booth)) booth.SetVoiceMode(MVSBooth.VOICE_MUFFLE); }
        public void _VoiceSilent() { if (Utilities.IsValid(booth)) booth.SetVoiceMode(MVSBooth.VOICE_SILENT); }

        private void Refresh()
        {
            if (!Utilities.IsValid(booth)) return;
            bool mine = booth.IsLocalWearer();
            bool worn = booth.IsWorn();
            if (Utilities.IsValid(statusText))
                statusText.text = mine ? youText : worn ? string.Format(otherFormat, booth.GetWearerName()) : freeText;
            if (Utilities.IsValid(hintText))
                hintText.text = mine ? youHint : worn ? otherHint : freeHint;
            if (Utilities.IsValid(wearerControls)) wearerControls.SetActive(mine);
            if (Utilities.IsValid(freeControls)) freeControls.SetActive(!worn);
            int mode = booth.GetVoiceMode();
            for (int i = 0; i < modeMarks.Length; i++)
                if (Utilities.IsValid(modeMarks[i])) modeMarks[i].SetActive(i == mode);
            for (int i = 0; i < modeLabels.Length; i++)
                if (Utilities.IsValid(modeLabels[i])) modeLabels[i].color = i == mode ? textColor : textDimColor;
        }
    }
}
