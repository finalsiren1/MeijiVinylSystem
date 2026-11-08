using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// One album in the browser (grid tile or list row). Wire its Button's onClick to <c>_Click</c>
    /// (and optionally an <see cref="MVSUIAnim"/>._Bounce). Every field except the cover is optional.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSAlbumCell : UdonSharpBehaviour
    {
        [SerializeField] private RawImage cover;
        [Tooltip("Shown when the album has no cover.")]
        [SerializeField] private GameObject noCover;
        [SerializeField] private TextMeshProUGUI title;
        [SerializeField] private TextMeshProUGUI artist;
        [Tooltip("Year · Genre (list view).")]
        [SerializeField] private TextMeshProUGUI detail;
        [Tooltip("Status label, e.g. 'Playing' / 'On the table' (list view).")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private Image statusDot;
        [SerializeField] private MVSUIAnim anim;

        [System.NonSerialized] public MVSAlbumBrowser browser;
        [System.NonSerialized] public int slot;

        private int album = -1;

        public int GetAlbum() { return album; }

        public void SetAlbum(int albumIndex, MVSLibrary library)
        {
            album = albumIndex;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Texture2D page = library.GetCoverPage(albumIndex);
            if (Utilities.IsValid(cover))
            {
                cover.enabled = Utilities.IsValid(page);
                if (Utilities.IsValid(page))
                {
                    Vector4 r = library.GetCoverRect(albumIndex);
                    cover.texture = page;
                    cover.uvRect = new Rect(r.x, r.y, r.z, r.w);
                }
            }
            if (Utilities.IsValid(noCover)) noCover.SetActive(!Utilities.IsValid(page));
            if (Utilities.IsValid(title)) title.text = library.GetTitle(albumIndex);
            if (Utilities.IsValid(artist)) artist.text = library.GetArtist(albumIndex);
            if (Utilities.IsValid(detail))
            {
                int year = library.GetYear(albumIndex);
                string genre = library.GetGenreName(library.GetGenre(albumIndex));
                string d = year > 0 ? year.ToString() : "";
                if (genre.Length > 0) d = d.Length > 0 ? d + "  ·  " + genre : genre;
                detail.text = d;
            }
        }

        public void SetStatus(string label, Color color, bool show)
        {
            if (Utilities.IsValid(statusDot))
            {
                statusDot.enabled = show;
                statusDot.color = color;
            }
            if (Utilities.IsValid(statusText)) statusText.text = show ? label : "";
        }

        public void Clear()
        {
            album = -1;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void Appear(float delay)
        {
            if (Utilities.IsValid(anim)) anim.Appear(delay);
        }

        public void _Click()
        {
            if (album >= 0 && Utilities.IsValid(browser)) browser.OnCellClicked(slot);
        }
    }
}
