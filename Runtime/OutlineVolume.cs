using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BillSSOutline
{
    [Serializable, VolumeComponentMenu("Post-processing/Custom/Outline")]
    public class OutlineVolume : VolumeComponent, IPostProcessComponent
    {
        public enum OutlineMode { FullScreen, SelectionOnly, Mixed }
        public enum OutlineAlgorithm { RobertsCross, Sobel }
        public enum DebugMode { None, Depth, Normals, Color, EdgeOnly, MaskOnly, Occlusion, SceneAlpha }

        public BoolParameter isActive = new BoolParameter(false);
        public EnumParameter<OutlineMode> mode = new EnumParameter<OutlineMode>(OutlineMode.FullScreen);

        [Header("Masking")]
        // Rendering layers (renderer.renderingLayerMask), shown by name in the inspector.
        // 2026-10-02: these were GameObject LayerMasks; Unity strips the bit of every unnamed
        // GameObject layer on load, which silently removed the player (bit 3) from the outline.
        // Optimization over stock (2acf5b7): default Nothing, not Everything - stock's -1 made
        // every camera redraw the whole scene into the selection mask by default.
        public RenderingLayerMaskParameter selectionLayer = new RenderingLayerMaskParameter(0u);
        public RenderingLayerMaskParameter occlusionLayer = new RenderingLayerMaskParameter(0u); // Objects that hide outline

        [Header("Settings")]
        public EnumParameter<DebugMode> debugMode = new EnumParameter<DebugMode>(DebugMode.None);
        public EnumParameter<OutlineAlgorithm> algorithm = new EnumParameter<OutlineAlgorithm>(OutlineAlgorithm.Sobel);

        public ClampedIntParameter thickness = new ClampedIntParameter(2, 1, 10);
        // 2026-10-02: thickness is in pixels at this short screen side and scales with the screen, so
        // a 3 px line on 1080 x 1920 stays the same weight on 720p, 1440p and tablets. 0 = raw pixels.
        public FloatParameter referenceShortSide = new FloatParameter(1080f);
        public ColorParameter outlineColor = new ColorParameter(new Color(0, 1, 0, 1), true, false, true);
        // 2026-10-02: 1 = the line takes the outlined object's own colour (darkened by tintDarken)
        // instead of outlineColor, a cartoon "coloured line". Selection modes only.
        public ClampedFloatParameter tintAmount = new ClampedFloatParameter(0f, 0f, 1f);
        public ClampedFloatParameter tintDarken = new ClampedFloatParameter(0.4f, 0f, 1f);

        public ClampedFloatParameter depthThreshold = new ClampedFloatParameter(1.5f, 0f, 10f);
        public ClampedFloatParameter normalThreshold = new ClampedFloatParameter(0.4f, 0f, 1f);
        public ClampedFloatParameter colorThreshold = new ClampedFloatParameter(0.2f, 0f, 1f);

        public BoolParameter useDepth = new BoolParameter(true);
        public BoolParameter useNormals = new BoolParameter(true);
        public BoolParameter useColor = new BoolParameter(false);

        public BoolParameter useDistanceFade = new BoolParameter(false);
        public FloatParameter fadeDistanceStart = new FloatParameter(0f);
        public FloatParameter fadeDistanceEnd = new FloatParameter(50f);

        public BoolParameter useHeightFade = new BoolParameter(false);
        public FloatParameter fadeHeightMin = new FloatParameter(0f);
        public FloatParameter fadeHeightMax = new FloatParameter(10f);

        public bool IsActive() => isActive.value;
        public bool IsTileCompatible() => false;
    }

    /// <summary>A volume parameter holding a rendering-layer mask (no interpolation: it switches).</summary>
    [Serializable]
    public sealed class RenderingLayerMaskParameter : VolumeParameter<RenderingLayerMask>
    {
        public RenderingLayerMaskParameter(uint value, bool overrideState = false)
            : base(new RenderingLayerMask { value = value }, overrideState) { }
    }
}
