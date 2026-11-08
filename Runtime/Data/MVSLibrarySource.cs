using UnityEngine;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Editor-only link between an <see cref="MVSLibraryData"/> asset and the <see cref="MVSLibrary"/>
    /// on the same GameObject. The library is baked automatically before entering Play Mode and before
    /// a VRChat build. Stripped from builds (IEditorOnly).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meiji Vinyl System/MVS Library Source")]
    public class MVSLibrarySource : MonoBehaviour, IEditorOnly
    {
        public MVSLibraryData data;
    }
}
