using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class NewURPRenderFeature : ScriptableRendererFeature
{
    [SerializeField] NewURPRenderFeatureSettings settings;
    NewURPRenderFeaturePass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new NewURPRenderFeaturePass(settings);

        // Configures where the render pass should be injected.
        // ■レンダリングパスを挿入する場所を設定
//      m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques; // ■差し替え
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        // You can request URP color texture and depth buffer as inputs by uncommenting the line below,
        // URP will ensure copies of these resources are available for sampling before executing the render pass.
        // Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
        // ■以下の行のコメントを外すことで、URPカラーテクスチャと深度バッファを入力として要求できます。
        // ■URPは、レンダリングパスを実行する前に、これらのリソースのコピーがサンプリング可能であることを保証します。
        // ■必要な場合のみコメントを外してください。特にモバイル端末やTBDR GPUではパフォーマンスに影響し、レンダリングパスが破綻する可能性があります。
        //m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);

        // You can request URP to render to an intermediate texture by uncommenting the line below.
        // Use this option for passes that do not support rendering directly to the backbuffer.
        // Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
        // ■以下の行のコメントを外すことで、URPに中間テクスチャへのレンダリングを要求できます。
        // ■バックバッファへの直接レンダリングをサポートしないパスにこのオプションを使用してください。
        // ■必要な場合のみコメントを外してください。特にモバイル端末やTBDR GPUではパフォーマンスに影響し、レンダリングパスが破綻する可能性があります。
        //m_ScriptablePass.requiresIntermediateTexture = true;
    }

    // ■ 追加(ASの削除の為)
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            m_ScriptablePass?.Cleanup();
            m_ScriptablePass = null;
        }
    }


    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    // ■ここで、レンダラーに1つまたは複数のレンダリングパスを挿入できます。
    // ■このメソッドは、カメラごとに1回、レンダラーを設定する際に呼び出されます。
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // ■追加: シーンビューとプレビューでは実行しない
        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera) return;

        renderer.EnqueuePass(m_ScriptablePass);
    }

    // Use this class to pass around settings from the feature to the pass
    // ■このクラスを使用して、フィーチャーからパスに設定を渡します
    [Serializable]
    public class NewURPRenderFeatureSettings
    {
        public RayTracingShader rayTracingShader = default!;// ■追加
    }

    class NewURPRenderFeaturePass : ScriptableRenderPass
    {
        readonly NewURPRenderFeatureSettings settings;

        RayTracingShader rayTracingShader;// ■ 追加
        RayTracingAccelerationStructure rayTracingAccelerationStructure;// ■ 追加


        public NewURPRenderFeaturePass(NewURPRenderFeatureSettings settings)
        {
            this.settings = settings;

            base.profilingSampler = new ProfilingSampler("NewURPRenderFeaturePass");
            rayTracingShader = settings.rayTracingShader;// ■ 追加
        }

        // ■ 追加
        public void Cleanup()
        {
            rayTracingAccelerationStructure?.Dispose();
        }


        // This class stores the data needed by the RenderGraph pass.
        // It is passed as a parameter to the delegate function that executes the RenderGraph pass.
        // ■このクラスは、RenderGraphパスに必要なデータを格納します。
        // ■RenderGraphパスを実行するデリゲート関数にパラメーターとして渡されます。
        private class PassData
        {
            public RayTracingShader rayTracingShader;// ■ 追加: レイトレシェーダー
            public TextureHandle output_ColorTexture;// ■ 追加: レイトレ結果を書き出すテクスチャ
            public TextureHandle camera_ColorTarget;// ■ 追加: カメラのカラーテクスチャ
            public RayTracingAccelerationStructure rayTracingAccelerationStructure;// ■ 追加: AS

            public Camera camera;// ■ 追加: カメラ
        }

        // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
        // It is used to execute draw commands.
        // ■この静的メソッドは、RenderGraphレンダーパスにRenderFuncデリゲートとして渡されます。
        // ■描画コマンドを実行するために使用されます.
//      static void ExecutePass(PassData data, RasterGraphContext context)
        static void ExecutePass(PassData data, UnsafeGraphContext context)// ■差し替え
        {
            // ■ 追加
            // UnsafeGraphContext からネイティブの CommandBuffer（GPUの命令を溜めるバッファ）を取得
            var native_cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            // レイトレシェーダーパスを設定
            native_cmd.SetRayTracingShaderPass(data.rayTracingShader, "NewURPRenderFeaturePass");
            // ★追加：レイトレシェーダーに加速構造を設定
            context.cmd.SetRayTracingAccelerationStructure(data.rayTracingShader,
                Shader.PropertyToID("SceneAS"), data.rayTracingAccelerationStructure);
            // レイトレシェーダーに出力テクスチャを設定
            context.cmd.SetRayTracingTextureParam(data.rayTracingShader, 
                Shader.PropertyToID("RenderTarget"), data.output_ColorTexture);
            // レイトレを実行
            context.cmd.DispatchRays(data.rayTracingShader, "MyRaygenShader", 
                (uint)data.camera.pixelWidth, (uint)data.camera.pixelHeight, 1, data.camera);
            // 結果をカメラに書き戻す
            native_cmd.Blit(data.output_ColorTexture, data.camera_ColorTarget);
        }

        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        // ■RecordRenderGraphは、RenderGraphハンドルにアクセスできる場所であり、これを通じてレンダーパスをグラフに追加できます。
        // ■FrameDataは、URPリソースにアクセスおよび管理できるコンテキストコンテナです。
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            const string passName = "Render Custom Pass";

            // ■ 追加
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            // ■ 追加: 現在のカメラで描画されたカラーフレームバッファを取得
            var colorTexture = resourceData.activeColorTexture;

            // ■ 追加: レイトレ結果を描き出すバッファを作成
            RenderTextureDescriptor rtdesc = cameraData.cameraTargetDescriptor;
            rtdesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            rtdesc.depthStencilFormat = GraphicsFormat.None;
            rtdesc.depthBufferBits = 0;
            rtdesc.enableRandomWrite = true;
            var resultTex = UniversalRenderer.CreateRenderGraphTexture(renderGraph, rtdesc, "_RayTracedColor", false);

            // ■ 追加: Acceleration Structure を作成
            if (rayTracingAccelerationStructure == null)
            {
                var settings = new RayTracingAccelerationStructure.Settings();
                settings.rayTracingModeMask = RayTracingAccelerationStructure.RayTracingModeMask.Everything;
                settings.managementMode = RayTracingAccelerationStructure.ManagementMode.Automatic;
                settings.layerMask = 255;
                rayTracingAccelerationStructure = new RayTracingAccelerationStructure(settings);

                rayTracingAccelerationStructure.Build();// 今回は静的に構築
            }


            // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
            // ■これにより、グラフにラスターレンダーパスが追加され、名前とExecutePass関数に渡されるデータタイプが指定されます。
//          using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            using (var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData))// ■差し替え
            {
                // Use this scope to set the required inputs and outputs of the pass and to
                // setup the passData with the required properties needed at pass execution time.
                // ■このスコープを使用して、パスの必要な入力と出力を設定し、
                // ■パス実行時に必要なプロパティでpassDataを設定します。

                // ■ 追加
                passData.rayTracingShader = rayTracingShader;
                passData.output_ColorTexture = resultTex;
                passData.camera_ColorTarget = colorTexture;
                passData.rayTracingAccelerationStructure = rayTracingAccelerationStructure;
                passData.camera = cameraData.camera;
                builder.UseTexture(passData.output_ColorTexture, AccessFlags.Write);
                builder.UseTexture(passData.camera_ColorTarget, AccessFlags.ReadWrite);

                // Make use of frameData to access resources and camera data through the dedicated containers.
                // Eg:
                // ■frameDataを利用して、専用のコンテナを通じてリソースとカメラデータにアクセスします。
                // ■例：
                // UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

//              UniversalResourceData resourceData = frameData.Get<UniversalResourceData>(); // ■ 追加: 上に移動

                // Setup pass inputs and outputs through the builder interface.
                // Eg:
                // ■builderインターフェースを通じてパスの入力と出力を設定します。
                // ■例：
                // builder.UseTexture(sourceTexture);
                // TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraData.cameraTargetDescriptor, "Destination Texture", false);// ■差し替え

                // This sets the render target of the pass to the active color texture. Change it to your own render target as needed.
                // ■パスのレンダーターゲットがアクティブなカラーテクスチャに設定されます。必要に応じて独自のレンダーターゲットに変更してください。
//              builder.SetRenderAttachment(resourceData.activeColorTexture, 0);// ■差し替え: 不要

                // Assigns the ExecutePass function to the render pass delegate. This will be called by the render graph when executing the pass.
                // ■レンダーパスデリゲートにExecutePass関数を割り当てます。これは、パスを実行する際にレンダーグラフによって呼び出されます。
//              builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));// ■差し替え
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => ExecutePass(data, context));
            }
        }
    }
}
