using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BillSSOutline
{
    public class OutlineFeature : ScriptableRendererFeature
    {

        /// <summary>Lets renderers drawn by hand (e.g. GPU-instanced foliage) draw into the selection
        /// mask. Receives the mask pass command buffer and the rendering layer mask being drawn.</summary>
        public static event Action<RasterCommandBuffer, uint> OnRenderFoliageMask;

        class LayerMaskPass : ScriptableRenderPass
        {
            // Renderers on these layers draw the mask with their own "OutlineSelectionMask" pass instead
            // of the override material. Needed for vertex-animated meshes (VAT): an override material
            // would drop their vertex animation and freeze the silhouette in bind pose. Regular
            // SkinnedMeshRenderers keep their skinning under an override material.
            private static uint MaterialDrivenRenderingLayerMask => OutlineOverrides.MaterialDrivenLayers;
            private static readonly ShaderTagId MaterialMaskShaderTag = new ShaderTagId("OutlineSelectionMask");

            private Material maskMaterial;
            private uint renderingLayerMask;
            private FilteringSettings filteringSettings;
            private readonly ShaderTagId[] shaderTags;
            private string profilerTag;
            private string textureName;

            public TextureHandle MaskTexture { get; private set; }

            public LayerMaskPass(string tag, string texName)
            {
                profilerTag = tag;
                textureName = texName;
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

                shaderTags = new ShaderTagId[]
                {
                    new ShaderTagId("UniversalForward"),
                    new ShaderTagId("UniversalForwardOnly"),
                    new ShaderTagId("SRPDefaultUnlit"),
                    new ShaderTagId("LightweightForward")
                };
                filteringSettings = new FilteringSettings(RenderQueueRange.all);
            }

            /// <summary>
            /// Chọn đối tượng vào mặt nạ theo RENDERING LAYER, không theo GameObject layer.
            ///
            /// GameObject layer là tầng VẬT LÝ: ma trận va chạm, raycast ngắm bắn, `WalkableGround`
            /// đều lọc theo nó. Mượn nó để chọn viền nghĩa là muốn viền một mảnh hình thì phải đổi
            /// tầng vật lý của mảnh đó — và những mảnh dùng chung GameObject với collider thì đành
            /// bỏ, để lộ lỗ hổng trên bóng nhân vật. `renderingLayerMask` là kênh THUẦN HÌNH ẢNH,
            /// nên chọn được đúng mọi renderer mà không đụng một byte nào của vật lý.
            /// </summary>
            public void Setup(uint mask)
            {
                this.renderingLayerMask = mask;
                filteringSettings.renderingLayerMask = mask;
                // GameObject layer để mở hoàn toàn: việc chọn lọc đã do rendering layer đảm nhiệm.
                filteringSettings.layerMask = ~0;
                if (maskMaterial == null) maskMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Outline/SelectionMask"));
            }

            private class MaskData
            {
                public RendererListHandle genericRendererList;
                public RendererListHandle vatRendererList;
                public bool drawGeneric;
                public bool drawVat;
                public TextureHandle maskDest;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                MaskTexture = TextureHandle.nullHandle;

                if (maskMaterial == null || renderingLayerMask == 0) return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.colorFormat = RenderTextureFormat.R8;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;

                TextureDesc texDesc = new TextureDesc(desc);
                texDesc.name = textureName;
                texDesc.clearBuffer = true;
                texDesc.clearColor = Color.black;

                MaskTexture = renderGraph.CreateTexture(texDesc);

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle depthTexture = resourceData.activeDepthTexture;

                uint vatBit = MaterialDrivenRenderingLayerMask;
                uint materialDrivenMask = renderingLayerMask & vatBit;
                uint genericMask = renderingLayerMask & ~vatBit;

                RendererListHandle genericRendererList = default;
                if (genericMask != 0)
                {
                    FilteringSettings genericFiltering = filteringSettings;
                    genericFiltering.renderingLayerMask = genericMask;
                    RendererListParams genericParams = new RendererListParams(
                        renderingData.cullResults,
                        // enableInstancing phải nói ra tường minh. DrawingSettings dựng bằng constructor
                        // này KHÔNG kế thừa thiết lập instancing của pipeline, nên pass mặt nạ vẽ từng
                        // renderer một trong khi pass hiển thị của cùng những object đó lại được gộp.
                        new DrawingSettings(shaderTags[0], new SortingSettings(cameraData.camera))
                        {
                            overrideMaterial = maskMaterial,
                            overrideMaterialPassIndex = 0,
                            enableInstancing = true
                        },
                        genericFiltering
                    );

                    for (int i = 1; i < shaderTags.Length; ++i)
                        genericParams.drawSettings.SetShaderPassName(i, shaderTags[i]);

                    genericRendererList = renderGraph.CreateRendererList(genericParams);
                }

                RendererListHandle vatRendererList = default;
                if (materialDrivenMask != 0)
                {
                    FilteringSettings vatFiltering = filteringSettings;
                    vatFiltering.renderingLayerMask = materialDrivenMask;
                    RendererListParams vatParams = new RendererListParams(
                        renderingData.cullResults,
                        // Keep the VAT material. Its dedicated pass samples the archetype VAT texture
                        // and the same per-instance animation/crossfade/dissolve values as the visible
                        // pass, so the mask follows the animated silhouette exactly.
                        //
                        // enableInstancing tường minh vì lý do như trên: nếu không, mặt nạ viền của
                        // đám đông tăng tuyến tính theo SỐ CON trong khi thân của chính chúng đã gộp.
                        new DrawingSettings(MaterialMaskShaderTag, new SortingSettings(cameraData.camera))
                        {
                            enableInstancing = true
                        },
                        vatFiltering
                    );
                    vatRendererList = renderGraph.CreateRendererList(vatParams);
                }

                using (var builder = renderGraph.AddRasterRenderPass<MaskData>(profilerTag, out var passData))
                {
                    passData.genericRendererList = genericRendererList;
                    passData.vatRendererList = vatRendererList;
                    passData.drawGeneric = genericMask != 0;
                    passData.drawVat = materialDrivenMask != 0;
                    passData.maskDest = MaskTexture;

                    if (passData.drawGeneric) builder.UseRendererList(passData.genericRendererList);
                    if (passData.drawVat) builder.UseRendererList(passData.vatRendererList);
                    builder.SetRenderAttachment(passData.maskDest, 0, AccessFlags.Write);

                    // The mask is single-sampled; a multisampled depth (High tier MSAA, or a preview
                    // camera drawing into an MSAA RenderTexture such as the gun turntable) cannot be
                    // bound with it - Render Graph threw on every such frame (device log 05/10). Those
                    // cameras draw the mask without depth occlusion instead.
                    if (depthTexture.IsValid() && renderGraph.GetTextureDesc(depthTexture).msaaSamples == MSAASamples.None)
                        builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Read);

                    // Copy LayerMask to local variable to avoid closure capture issues
                    uint currentMask = renderingLayerMask;

                    builder.SetRenderFunc((MaskData data, RasterGraphContext context) =>
                    {
                        if (data.drawGeneric) context.cmd.DrawRendererList(data.genericRendererList);
                        if (data.drawVat) context.cmd.DrawRendererList(data.vatRendererList);

                        // Trigger Foliage Rendering
                        OnRenderFoliageMask?.Invoke(context.cmd, currentMask);
                    });
                }
            }
            public void Dispose() { CoreUtils.Destroy(maskMaterial); }
        }

        class OutlinePass : ScriptableRenderPass
        {
            private Material material;
            private OutlineVolume volumeSettings;
            private const string ShaderName = "Hidden/FullScreen/Outline";

            private static readonly int ThicknessID = Shader.PropertyToID("_Thickness");
            private static readonly int ColorID = Shader.PropertyToID("_OutlineColor");
            private static readonly int DepthThresholdID = Shader.PropertyToID("_DepthThreshold");
            private static readonly int NormalThresholdID = Shader.PropertyToID("_NormalThreshold");
            private static readonly int ColorThresholdID = Shader.PropertyToID("_ColorThreshold");
            private static readonly int DebugModeID = Shader.PropertyToID("_DebugMode");
            private static readonly int SelectionMaskID = Shader.PropertyToID("_SelectionMaskTexture");
            private static readonly int OcclusionMaskID = Shader.PropertyToID("_OcclusionMaskTexture");
            private static readonly int FadeParamsID = Shader.PropertyToID("_FadeParams");
            private static readonly int TintAmountID = Shader.PropertyToID("_TintAmount");
            private static readonly int TintDarkenID = Shader.PropertyToID("_TintDarken");

            private LayerMaskPass selectionMaskPass;
            private LayerMaskPass occlusionMaskPass;

            private class PassData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle destination;
                public TextureHandle mask;
                public TextureHandle occlusion;
            }

            // Before the transparents (03/10): with the mask in the colour alpha, particles, blob
            // shadows and other blended passes would write into that alpha. The lines now sit under
            // the effects instead of on top of them.
            public OutlinePass() { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; }

            private bool alphaMask;
            private static readonly int AlphaMaskID = Shader.PropertyToID("_AlphaMask");

            /// <param name="selectPass">Null when no layer is left for the mask texture this frame.</param>
            public void SetupReference(LayerMaskPass selectPass, LayerMaskPass occludePass, bool alphaMaskOn)
            {
                this.selectionMaskPass = selectPass;
                this.occlusionMaskPass = occludePass;
                this.alphaMask = alphaMaskOn;
            }

            private bool UpdateMaterial()
            {
                var stack = VolumeManager.instance.stack;
                volumeSettings = stack.GetComponent<OutlineVolume>();
                if (volumeSettings == null || !volumeSettings.IsActive() || OutlineOverrides.Hidden) return false;
                if (material == null) material = CoreUtils.CreateEngineMaterial(Shader.Find(ShaderName));
                if (material == null) return false;

                material.SetColor(ColorID, OutlineOverrides.Colour ?? volumeSettings.outlineColor.value);
                material.SetFloat(DepthThresholdID, volumeSettings.depthThreshold.value);
                material.SetFloat(NormalThresholdID, volumeSettings.normalThreshold.value);
                material.SetFloat(ColorThresholdID, volumeSettings.colorThreshold.value);
                material.SetInt(DebugModeID, (int)volumeSettings.debugMode.value);
                material.SetFloat(AlphaMaskID, alphaMask ? 1f : 0f);
                material.SetFloat(TintAmountID, OutlineOverrides.Tint?.x ?? volumeSettings.tintAmount.value);
                material.SetFloat(TintDarkenID, OutlineOverrides.Tint?.y ?? volumeSettings.tintDarken.value);

                material.SetVector(FadeParamsID, new Vector4(
                    volumeSettings.fadeDistanceStart.value,
                    volumeSettings.fadeDistanceEnd.value,
                    volumeSettings.fadeHeightMin.value,
                    volumeSettings.fadeHeightMax.value
                ));

                SetKeyword("USE_DEPTH", volumeSettings.useDepth.value);
                SetKeyword("USE_NORMALS", UseNormals(volumeSettings));
                SetKeyword("USE_COLOR", volumeSettings.useColor.value);
                SetKeyword("ALGO_SOBEL", volumeSettings.algorithm.value == OutlineVolume.OutlineAlgorithm.Sobel);
                SetKeyword("ALGO_ROBERTS", volumeSettings.algorithm.value == OutlineVolume.OutlineAlgorithm.RobertsCross);

                SetKeyword("USE_DISTANCE_FADE", volumeSettings.useDistanceFade.value);
                SetKeyword("USE_HEIGHT_FADE", volumeSettings.useHeightFade.value);
                SetKeyword("USE_OCCLUSION_MASK", volumeSettings.occlusionLayer.value.value != 0u);

                var mode = volumeSettings.mode.value;
                SetKeyword("OUTLINE_FULL", mode == OutlineVolume.OutlineMode.FullScreen);
                SetKeyword("OUTLINE_SELECTION", mode == OutlineVolume.OutlineMode.SelectionOnly);
                SetKeyword("OUTLINE_MIXED", mode == OutlineVolume.OutlineMode.Mixed);

                return true;
            }

            /// <summary>Line width in pixels of this camera target: the authored width is meant for the
            /// reference short side and scales with the actual one (never below one pixel).</summary>
            private float ScaledThickness(UniversalCameraData cameraData)
            {
                float px = OutlineOverrides.Thickness ?? volumeSettings.thickness.value;
                float reference = volumeSettings.referenceShortSide.value;
                if (reference <= 0f) return px;
                var d = cameraData.cameraTargetDescriptor;
                float shortSide = Mathf.Min(d.width, d.height);
                float perCamera = cameraData.camera != null && cameraData.camera.TryGetComponent(out OutlineCameraWidth w) ? w.multiplier : 1f;
                return Mathf.Max(1f, px * shortSide / reference * perCamera);
            }

            private void SetKeyword(string k, bool v) { if (v) material.EnableKeyword(k); else material.DisableKeyword(k); }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!UpdateMaterial()) return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                material.SetFloat(ThicknessID, ScaledThickness(cameraData));
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle source = resourceData.activeColorTexture;

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                TextureDesc texDesc = new TextureDesc(desc);
                texDesc.name = "OutlineTemp";
                texDesc.clearBuffer = false;
                // The result becomes the camera colour that transparents then draw into next to the
                // camera depth, so it keeps the camera's sample count: a single-sampled copy under an
                // MSAA camera (High tier, the gun turntable's 4x texture) threw in DrawTransparentObjects
                // on every frame (device log 07/10).
                texDesc.msaaSamples = renderGraph.GetTextureDesc(source).msaaSamples;

                TextureHandle tempTexture = renderGraph.CreateTexture(texDesc);

                TextureHandle maskHandle = (selectionMaskPass != null) ? selectionMaskPass.MaskTexture : TextureHandle.nullHandle;
                TextureHandle occlusionHandle = (occlusionMaskPass != null) ? occlusionMaskPass.MaskTexture : TextureHandle.nullHandle;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Composite", out var passData))
                {
                    passData.material = material;
                    passData.source = source;
                    passData.destination = tempTexture;
                    passData.mask = maskHandle;
                    passData.occlusion = occlusionHandle;

                    builder.UseTexture(passData.source, AccessFlags.Read);
                    if (passData.mask.IsValid()) builder.UseTexture(passData.mask, AccessFlags.Read);
                    if (passData.occlusion.IsValid()) builder.UseTexture(passData.occlusion, AccessFlags.Read);

                    builder.SetRenderAttachment(passData.destination, 0, AccessFlags.Write);

                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        if (data.mask.IsValid()) data.material.SetTexture(SelectionMaskID, data.mask);
                        else data.material.SetTexture(SelectionMaskID, Texture2D.blackTexture);
                        if (data.occlusion.IsValid()) data.material.SetTexture(OcclusionMaskID, data.occlusion);
                        else data.material.SetTexture(OcclusionMaskID, Texture2D.blackTexture);

                        Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                    });
                }

                resourceData.cameraColor = tempTexture;
            }
            public void Dispose() { CoreUtils.Destroy(material); }
        }

        private LayerMaskPass selectionPass;
        private LayerMaskPass occlusionPass;
        private OutlinePass outlinePass;

        private static readonly int AlphaLayersID = Shader.PropertyToID("_BillOutlineAlphaLayers");

        /// <summary>Rendering layers whose shaders write the outline mask into the colour alpha
        /// (see Shaders/OutlineAlphaMask.hlsl). They skip the second mask draw.</summary>
        private static uint AlphaCapableLayers => AlphaMaskEnabled ? OutlineOverrides.AlphaCapableLayers : 0u;

        /// <summary>False sends every outlined layer back through the mask redraw (A/B comparisons).</summary>
        public static bool AlphaMaskEnabled = true;

        /// <summary>Normal edges need URP's DepthNormals prepass, which draws every opaque object a
        /// second time. <see cref="OutlineOverrides.AllowNormals"/> lets a game turn them off on
        /// low-end devices.</summary>
        internal static bool UseNormals(OutlineVolume settings) =>
            settings.useNormals.value && (OutlineOverrides.AllowNormals == null || OutlineOverrides.AllowNormals());

        public override void Create()
        {
            selectionPass = new LayerMaskPass("Outline Selection Mask", "_SelectionMaskTexture");
            occlusionPass = new LayerMaskPass("Outline Occlusion Mask", "_OcclusionMaskTexture");
            outlinePass = new OutlinePass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            // Every camera sets the alpha path for its own frame: off unless this one turns it on.
            Shader.SetGlobalFloat(AlphaLayersID, 0f);
            if (cameraData.cameraType != CameraType.Game ||
                cameraData.renderType != CameraRenderType.Base)
                return;

            var stack = VolumeManager.instance.stack;
            var settings = stack.GetComponent<OutlineVolume>();

            if (settings != null && settings.IsActive() && !OutlineOverrides.Hidden)
            {
                // Optimization over stock (2acf5b7): the selection redraw only runs when a mode
                // that actually consumes the selection mask is active. Stock enqueued it even in
                // FullScreen mode, re-rendering every selected-layer object into an unused mask.
                bool wantsSelection = settings.mode.value != OutlineVolume.OutlineMode.FullScreen
                                      && settings.selectionLayer.value.value != 0u;
                uint selection = wantsSelection ? settings.selectionLayer.value.value : 0u;

                // 03/10: the layers whose shaders write the mask into the colour alpha skip the
                // mask redraw (OutlineAlphaMask.hlsl). Only on the game view itself: a capture into
                // a texture keeps its real alpha, and an HDR colour format without alpha cannot
                // carry it.
                uint alphaLayers = 0u;
                if (selection != 0u && cameraData.targetTexture == null &&
                    UnityEngine.Experimental.Rendering.GraphicsFormatUtility.HasAlphaChannel(cameraData.cameraTargetDescriptor.graphicsFormat))
                    alphaLayers = selection & AlphaCapableLayers;
                Shader.SetGlobalFloat(AlphaLayersID, alphaLayers);

                uint maskLayers = selection & ~alphaLayers;
                if (maskLayers != 0u)
                {
                    selectionPass.Setup(maskLayers);
                    renderer.EnqueuePass(selectionPass);
                }
                bool wantsOcclusion = settings.occlusionLayer.value.value != 0u;
                // A pass that is not enqueued this frame keeps the handle of an older graph: only
                // hand the composite the passes that actually record.
                outlinePass.SetupReference(maskLayers != 0u ? selectionPass : null,
                    wantsOcclusion ? occlusionPass : null, alphaLayers != 0u);
                if (wantsOcclusion)
                {
                    occlusionPass.Setup(settings.occlusionLayer.value.value);
                    renderer.EnqueuePass(occlusionPass);
                }

                // Request only the renderer inputs the active feature set needs: the DepthNormals
                // prepass is pure waste when normal edges are disabled.
                //
                // Color is NOT optional though, and dropping it was the bug that made the whole
                // effect silently vanish. The composite blits the active colour target into a temp
                // texture, so that target has to be a sampleable RenderGraph texture. With no input
                // requested, URP is free to render the game camera straight into the backbuffer —
                // and a backbuffer cannot be sampled, so `RecordRenderGraph` hit its
                // `isActiveTargetBackBuffer` guard and returned without drawing anything. The
                // profile looked perfectly configured while rendering nothing at all.
                //
                // Asking for Color only while the outline is active makes URP allocate the
                // intermediate colour target exactly when it is needed, instead of forcing
                // "Always Intermediate" on both renderer assets for every camera.
                var inputs = ScriptableRenderPassInput.Color;
                if (settings.useDepth.value || settings.useDistanceFade.value || settings.useHeightFade.value)
                    inputs |= ScriptableRenderPassInput.Depth;
                if (UseNormals(settings))
                    inputs |= ScriptableRenderPassInput.Normal;
                outlinePass.ConfigureInput(inputs);

                renderer.EnqueuePass(outlinePass);
            }
        }

        protected override void Dispose(bool disposing)
        {
            selectionPass.Dispose();
            occlusionPass.Dispose();
            outlinePass.Dispose();
        }
    }
}
