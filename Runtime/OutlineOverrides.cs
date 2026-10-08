using System;
using UnityEngine;

namespace BillSSOutline
{
    /// <summary>
    /// Game-side hooks for the outline. The volume profile stays the source of truth; anything set
    /// here is a runtime override. The look overrides reset at the start of every Play session.
    /// </summary>
    public static class OutlineOverrides
    {
        /// <summary>Rendering layers drawn into the mask with their own <c>OutlineSelectionMask</c>
        /// pass instead of the override material (vertex-animated meshes such as VAT).</summary>
        public static uint MaterialDrivenLayers;

        /// <summary>Rendering layers whose shaders call <c>BillOutlineAlpha()</c> and so write the mask
        /// into the colour alpha. They skip the second mask draw.</summary>
        public static uint AlphaCapableLayers;

        /// <summary>Return false to drop normal edges (and URP's DepthNormals prepass), e.g. on
        /// low-end devices. Null means always allowed.</summary>
        public static Func<bool> AllowNormals;

        /// <summary>Look overrides for QA and A/B comparisons. Null = use the volume value.</summary>
        public static Color? Colour;
        /// <summary>x = how much the line takes the object's own colour (0-1), y = its darkening.</summary>
        public static Vector2? Tint;
        public static int? Thickness;
        /// <summary>True skips every outline pass.</summary>
        public static bool Hidden;

        public static void ClearLook()
        {
            Colour = null; Tint = null; Thickness = null; Hidden = false;
        }

        // Statics survive Play Mode exit when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => ClearLook();
    }
}
