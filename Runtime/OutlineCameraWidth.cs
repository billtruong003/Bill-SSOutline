using UnityEngine;

namespace BillSSOutline
{
    /// <summary>
    /// Outline width multiplier for one camera. Put it on a camera that renders into a texture shown
    /// smaller or larger on screen (a menu character preview, a minimap) so its line matches the
    /// main camera.
    /// </summary>
    [DisallowMultipleComponent]
    public class OutlineCameraWidth : MonoBehaviour
    {
        public float multiplier = 1f;
    }
}
