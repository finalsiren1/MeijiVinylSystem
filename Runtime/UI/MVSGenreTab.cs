using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// A genre filter entry. The browser clones one template per library genre at runtime, so genres added
    /// in the Library Editor appear automatically. Wire the Button's onClick to <c>_Click</c>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSGenreTab : UdonSharpBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private TextMeshProUGUI count;
        [Tooltip("Shown on the selected tab (e.g. a focus frame).")]
        [SerializeField] private GameObject selectedMark;
        [SerializeField] private Color selectedColor = new Color(0.941f, 0.949f, 0.965f, 1f);
        [SerializeField] private Color normalColor = new Color(0.588f, 0.612f, 0.659f, 1f);

        [System.NonSerialized] public MVSAlbumBrowser browser;
        /// <summary>Library genre index, or MVSAlbumBrowser.GENRE_ALL (-1) / GENRE_UNSORTED (-2).</summary>
        [System.NonSerialized] public int genre = -1;

        public void Setup(MVSAlbumBrowser owner, int genreIndex, string name, int albums)
        {
            browser = owner;
            genre = genreIndex;
            if (Utilities.IsValid(label)) label.text = name;
            if (Utilities.IsValid(count)) count.text = albums.ToString();
        }

        public void SetSelected(bool selected)
        {
            if (Utilities.IsValid(selectedMark)) selectedMark.SetActive(selected);
            if (Utilities.IsValid(label)) label.color = selected ? selectedColor : normalColor;
        }

        public void _Click()
        {
            if (Utilities.IsValid(browser)) browser.SelectGenre(genre);
        }
    }
}
