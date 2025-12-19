Shader "Custom/NewUnlitUniversalRenderPipelineShader"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
    }

    SubShader
    {
        Tags { "LightMode" = "NewURPRenderFeaturePass"}// ★追加
//      Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}// ★削除

        Pass
        {
            Name "NewURPRenderFeaturePass"// ★追加: SetRayTracingShaderPassで設定したパス名

            HLSLPROGRAM

//          #pragma vertex vert// ★削除
//          #pragma fragment frag// ★削除
            #pragma raytracing surface_shader// ★追加

//          #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"// ★削除
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"// ★追加
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"// ★追加
            #include "UnityRaytracingMeshUtils.cginc"// ★追加

#if false // ★削除
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }
            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }
#endif

            // ★以降追加

            struct RayPayload
            {
                bool hit;
                float3 radiance;
            };
            
            struct AttributeData
            {
                float2 barycentrics;
            };

            RaytracingAccelerationStructure SceneAS;

            struct Vertex
            {
                float3 position;
                float3 normal;
                float4 tangent;
                float2 texCoord0;
                float2 texCoord1;
                float2 texCoord2;
                float2 texCoord3;
                float4 color;
            };

            // Unity ビルトイン関数を使って頂点データを得る関数
            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.position  = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
                v.normal    = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                v.tangent   = UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTangent);
                v.texCoord0 = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord0);
                v.texCoord1 = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord1);
                v.texCoord2 = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord2);
                v.texCoord3 = UnityRayTracingFetchVertexAttribute2(vertexIndex, kVertexAttributeTexCoord3);
                v.color     = UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeColor);
                return v;
            }

            // 重心座標での補間関数
            float2 barycentricInterpolate2(float2 v0, float2 v1, float2 v2, float3 barycentrics)
            {
                return v0 * barycentrics.x + v1 * barycentrics.y + v2 * barycentrics.z;
            }

            float3 barycentricInterpolate3(float3 v0, float3 v1, float3 v2, float3 barycentrics)
            {
                return v0 * barycentrics.x + v1 * barycentrics.y + v2 * barycentrics.z;
            }

            float4 barycentricInterpolate4(float4 v0, float4 v1, float4 v2, float3 barycentrics)
            {
                return v0 * barycentrics.x + v1 * barycentrics.y + v2 * barycentrics.z;
            }

            // レイトレのヒットした位置から頂点データを補間する関数
            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                v.position  = barycentricInterpolate3(v0.position,  v1.position,  v2.position,  barycentrics);
                v.normal    = barycentricInterpolate3(v0.normal,    v1.normal,    v2.normal,    barycentrics);
                v.tangent   = barycentricInterpolate4(v0.tangent,   v1.tangent,   v2.tangent,   barycentrics);
                v.texCoord0 = barycentricInterpolate2(v0.texCoord0, v1.texCoord0, v2.texCoord0, barycentrics);
                v.texCoord1 = barycentricInterpolate2(v0.texCoord1, v1.texCoord1, v2.texCoord1, barycentrics);
                v.texCoord2 = barycentricInterpolate2(v0.texCoord2, v1.texCoord2, v2.texCoord2, barycentrics);
                v.texCoord3 = barycentricInterpolate2(v0.texCoord3, v1.texCoord3, v2.texCoord3, barycentrics);
                v.color     = barycentricInterpolate4(v0.color,     v1.color,     v2.color,     barycentrics);
                return v;
            }

            [shader("closesthit")]
            void ClosestHitMain(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
            {
                // レイトレの交点から頂点インデックスを取得
                uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());

                // 頂点インデックスから三角形の3頂点を取得
                Vertex v0, v1, v2;
                v0 = FetchVertex(triangleIndices.x);
                v1 = FetchVertex(triangleIndices.y);
                v2 = FetchVertex(triangleIndices.z);

                // 3頂点からの情報を交点の重心座標で補間
                float3 barycentricCoords = float3(
                    1.0 - attribs.barycentrics.x - attribs.barycentrics.y, 
                    attribs.barycentrics.x, 
                    attribs.barycentrics.y);
                Vertex v = InterpolateVertices(v0, v1, v2, barycentricCoords);

                // ベースマップのテクスチャから色を取得
                // _BaseMap などのプロパティは LitInput.hlsl を include すれば使用できる
                float3 color = _BaseColor.rgb * SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, v.texCoord0, 0).rgb;
                // 簡易ライティング計算
                float3 normal = TransformObjectToWorldNormal(v.normal);
                color *= lerp(0.1, 1.0, max(0, dot(normal, _MainLightPosition)));// [0.1, 1.0]の範囲
                color = _MainLightColor * color;

                payload.hit = true;
                payload.radiance = color;
//              payload.radiance = v.normal * 0.5 + 0.5;// 法線を色に変換して表示する場合
            }
            ENDHLSL
        }
    }
}
