using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public class NewURPRenderFeature : ScriptableRendererFeature
{
    [SerializeField] NewURPRenderFeatureSettings settings;
    NewURPRenderFeaturePass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new NewURPRenderFeaturePass(settings);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        // You can request URP color texture and depth buffer as inputs by uncommenting the line below,
        // URP will ensure copies of these resources are available for sampling before executing the render pass.
        // Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
        //m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);

        // You can request URP to render to an intermediate texture by uncommenting the line below.
        // Use this option for passes that do not support rendering directly to the backbuffer.
        // Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
        //m_ScriptablePass.requiresIntermediateTexture = true;
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            m_ScriptablePass?.Cleanup();
            m_ScriptablePass = null;
        }
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera) return;

        renderer.EnqueuePass(m_ScriptablePass);
    }

    // Use this class to pass around settings from the feature to the pass
    [Serializable]
    public class NewURPRenderFeatureSettings
    {
        public RayTracingShader rayTracingShader = default!;
    }

    class NewURPRenderFeaturePass : ScriptableRenderPass
    {
        readonly NewURPRenderFeatureSettings settings;

        RayTracingShader rayTracingShader;
        RayTracingAccelerationStructure rayTracingAccelerationStructure;

        public NewURPRenderFeaturePass(NewURPRenderFeatureSettings settings)
        {
            this.settings = settings;

            base.profilingSampler = new ProfilingSampler("NewURPRendererFeaturePass");
            rayTracingShader = settings.rayTracingShader;
        }

        public void Cleanup()
        {
            rayTracingAccelerationStructure?.Dispose();
        }

        // This class stores the data needed by the RenderGraph pass.
        // It is passed as a parameter to the delegate function that executes the RenderGraph pass.
        private class PassData
        {
            public RayTracingShader rayTracingShader;
            public TextureHandle output_ColorTexture;
            public TextureHandle camera_ColorTarget;
            public RayTracingAccelerationStructure rayTracingAccelerationStructure;

            public Camera camera;
        }

        // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
        // It is used to execute draw commands.
        static void ExecutePass(PassData data, UnsafeGraphContext context)
        {
            CommandBuffer native_cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            
            native_cmd.SetRayTracingShaderPass(data.rayTracingShader, "NewURPRenderFeaturePass");
            
            context.cmd.SetRayTracingTextureParam(data.rayTracingShader,
                Shader.PropertyToID("RenderTarget"), data.output_ColorTexture);

            context.cmd.DispatchRays(data.rayTracingShader, "MyRaygenShader",
                (uint)data.camera.pixelWidth, (uint)data.camera.pixelHeight, 1, data.camera);

            native_cmd.Blit(data.output_ColorTexture, data.camera_ColorTarget);
        }

        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "Render Custom Pass";

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            var colorTexture = resourceData.activeColorTexture;

            RenderTextureDescriptor rtdesc = cameraData.cameraTargetDescriptor;
            rtdesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            rtdesc.depthStencilFormat = GraphicsFormat.None;
            rtdesc.depthBufferBits = 0;
            rtdesc.enableRandomWrite = true;
            var resultTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, rtdesc, "_RayTracedColor", false);

            if(rayTracingAccelerationStructure==null)
            {
                var settings = new RayTracingAccelerationStructure.Settings();
                settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
                settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
                settings.layerMask = 255;
                rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);

                rayTracingAccelerationStructure.Build();
            }

            // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
            using (var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData))
            {
                // Use this scope to set the required inputs and outputs of the pass and to
                // setup the passData with the required properties needed at pass execution time.

                passData.rayTracingShader = rayTracingShader;
                passData.output_ColorTexture = resultTex;
                passData.camera_ColorTarget = colorTexture;
                passData.rayTracingAccelerationStructure = rayTracingAccelerationStructure;
                passData.camera = cameraData.camera;
                builder.UseTexture(passData.output_ColorTexture, AccessFlags.Write);
                builder.UseTexture(passData.camera_ColorTarget, AccessFlags.ReadWrite);


                // Make use of frameData to access resources and camera data through the dedicated containers.
                // Eg:
                // UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                //UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                // Setup pass inputs and outputs through the builder interface.
                // Eg:
                // builder.UseTexture(sourceTexture);
                // TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraData.cameraTargetDescriptor, "Destination Texture", false);

                // This sets the render target of the pass to the active color texture. Change it to your own render target as needed.
                //builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                // Assigns the ExecutePass function to the render pass delegate. This will be called by the render graph when executing the pass.
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
    }
}
