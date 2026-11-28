// Buildings of Horizonte. Every model is one mesh with one material: the real textures of all the models live in two
// texture arrays (colour and relief) and each vertex says which layer it uses (TEXCOORD1.z) and where its flat colour,
// finish and glow are in small palette textures (TEXCOORD1.xy). The colour is palette x texture x TEXCOORD1.w, so a wall
// repainted in a facade colour keeps the grain of its plaster. One draw call per building, as before.
Shader "Horizonte/Edificios"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Paleta", 2D) = "white" {}
        _BaseColor("Tinte", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _MetallicGlossMap("Paleta de acabado", 2D) = "black" {}
        [NoScaleOffset] _EmissionMap("Paleta de luz", 2D) = "black" {}
        [HDR] _EmissionColor("Luz", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _Albedo("Texturas", 2DArray) = "" {}
        [NoScaleOffset] _Normals("Relieves", 2DArray) = "" {}
        _BumpScale("Relieve", Float) = 1
        _Alpha("Opacidad", Range(0, 1)) = 1
        [HideInInspector] _SrcBlend("", Float) = 1
        [HideInInspector] _DstBlend("", Float) = 0
        [HideInInspector] _ZWrite("", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            half _BumpScale;
            half _Alpha;
        CBUFFER_END

        TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MetallicGlossMap);
        TEXTURE2D(_EmissionMap);
        TEXTURE2D_ARRAY(_Albedo);       SAMPLER(sampler_Albedo);
        TEXTURE2D_ARRAY(_Normals);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment

            #pragma shader_feature_local_fragment _EMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 look : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 look : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half3 normalWS : TEXCOORD3;
                half4 tangentWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                half3 sh : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uv = input.uv;
                o.look = input.look;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.sh = SampleSH(n.normalWS);
                return o;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float layer = input.look.z;
                half4 palette = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.look.xy);
                half4 finish = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, input.look.xy);
                half3 glow = SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, input.look.xy).rgb;
                half4 texel = SAMPLE_TEXTURE2D_ARRAY(_Albedo, sampler_Albedo, input.uv, layer);
                half3 relief = SAMPLE_TEXTURE2D_ARRAY(_Normals, sampler_Albedo, input.uv, layer).rgb * 2 - 1;
                relief.xy *= _BumpScale;
                relief = normalize(relief);

                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(relief, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                SurfaceData s = (SurfaceData)0;
                s.albedo = saturate(palette.rgb * texel.rgb * input.look.w) * _BaseColor.rgb;
                s.metallic = finish.r;
                s.smoothness = finish.a;
                s.occlusion = 1;
                s.alpha = _Alpha;
                s.normalTS = relief;
                #if defined(_EMISSION)
                    // Lit interiors glow through their own picture; lamps in their colour.
                    s.emission = glow * lerp(1, texel.rgb * 1.6, step(.5, layer)) * _EmissionColor.rgb;
                #endif

                InputData d = (InputData)0;
                d.positionWS = input.positionWS;
                d.positionCS = input.positionCS;
                d.normalWS = normalWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                d.fogCoord = input.fogFactor;
                d.bakedGI = input.sh;
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 colour = UniversalFragmentPBR(d, s);
                colour.rgb = MixFog(colour.rgb, d.fogCoord);
                colour.a = _Alpha;
                return colour;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 Fragment() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 Vertex(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half Fragment() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Fragment(Varyings input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0); }
            ENDHLSL
        }
    }
}
