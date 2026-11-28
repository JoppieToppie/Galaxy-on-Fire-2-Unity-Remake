// The screen recorder's frame packer (UI.ScreenRecorder; docs/notes/platforms.md "Screen recorder"): the captured picture
// scaled into one target of _Size.x by 1.5 _Size.y holding planar 4:2:0 YUV as the encoders take it: the Y plane, then
// the U plane, then the V plane (each chroma sample the 2 x 2 pixels' average through the bilinear sampler), limited
// range, the image's top row first. Written by the target's uv (Unity's convention: uv.y 0 is the first row a readback
// returns), so the same packing comes out on D3D, Vulkan, Metal and GLES; _Flip turns a source whose rows start at the
// top (ScreenCapture on a graphicsUVStartsAtTop API) the right way up.
//   _Hdr 0  SDR: an R8 target, BT.709 8-bit from gamma-encoded colour: the back buffer's copy is that already, a camera's
//           linear colour (RecorderScenePass) is encoded first (_Linear)
//   _Hdr 1  HDR10: an R16 target, BT.2020 10-bit of PQ-coded colour (the 10 bits high, as P010 has them): an HDR screen's
//           copy holds PQ codes already; a camera's linear colour is BT.2020 nits after URP's HDR tonemapping, or BT.709
//           relative to paper white without it (_SceneNits 0), and is PQ-coded here
//   _Hdr 2  SDR from an HDR screen (no 10-bit HEVC encoder): the PQ decoded, to BT.709 relative to paper white, an
//           shoulder over 80 % of paper white, sRGB-encoded, then as _Hdr 0
Shader "Hidden/GoF2/RecorderYuv"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"   // LinearToSRGB

            TEXTURE2D(_MainTex);   // (sampler_LinearClamp: Core.hlsl's)
            float2 _Size;          // the picture's width and height (multiples of 4)
            float _Flip;
            float _Linear;         // 1: the source holds linear colour (the cameras' target, RecorderScenePass)
            float _Hdr;            // 0 SDR, 1 HDR10, 2 SDR from an HDR screen
            float _SceneNits;      // HDR, a camera's colour: 1 BT.2020 nits (tonemapped), 0 BT.709 relative to paper white
            float _PaperWhite;     // nits
            float _Peak;           // nits (the metadata's; not used by the packing)

            static const float3x3 Rec709To2020 = { 0.6274040, 0.3292820, 0.0433136,
                                                   0.0690970, 0.9195400, 0.0113612,
                                                   0.0163916, 0.0880132, 0.8955950 };
            static const float3x3 Rec2020To709 = { 1.6604910, -0.5876411, -0.0728499,
                                                  -0.1245505,  1.1328999, -0.0083494,
                                                  -0.0181508, -0.1005789,  1.1187297 };

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes a)
            {
                Varyings v;
                v.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                v.uv = a.uv;
                return v;
            }

            // SMPTE ST 2084 between nits and the code value 0..1.
            float3 NitsToPq(float3 nits)
            {
                float3 y = pow(saturate(nits / 10000.0), 0.1593017578125);
                return pow((0.8359375 + 18.8515625 * y) / (1.0 + 18.6875 * y), 78.84375);
            }

            float3 PqToNits(float3 e)
            {
                float3 p = pow(saturate(e), 1.0 / 78.84375);
                return 10000.0 * pow(max(p - 0.8359375, 0.0) / (18.8515625 - 18.6875 * p), 1.0 / 0.1593017578125);
            }

            float3 Sample(float2 imageUv)   // imageUv: x right, y down from the image's top
            {
                float2 uv = float2(imageUv.x, _Flip > 0.5 ? imageUv.y : 1.0 - imageUv.y);
                return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_LinearClamp, uv, 0).rgb;
            }

            /// The pixel's colour as the video codes it in 'mode' (_Hdr's): gamma-encoded BT.709 (SDR) or PQ-coded BT.2020 (HDR10).
            float3 Coded(float2 imageUv, float mode)
            {
                float3 c = Sample(imageUv);
                if (mode < 0.5) return _Linear > 0.5 ? LinearToSRGB(saturate(c)) : saturate(c);
                // HDR: the light in BT.2020 nits first.
                float3 nits = _Linear < 0.5 ? PqToNits(c)
                            : _SceneNits > 0.5 ? max(c, 0.0) : mul(Rec709To2020, max(c, 0.0)) * _PaperWhite;
                if (mode < 1.5) return NitsToPq(nits);
                // SDR from HDR: paper white is SDR's white; the light over 80 % of it rolls off into the last 20 % (a
                // shoulder), the rest stays as it is.
                float3 rgb = max(mul(Rec2020To709, nits / _PaperWhite), 0.0);
                float l = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                if (l > 0.8) rgb *= (0.8 + 0.2 * (1.0 - exp(-(l - 0.8) / 0.2))) / l;
                return LinearToSRGB(saturate(rgb));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                uint w = (uint)_Size.x, h = (uint)_Size.y;
                uint col = min((uint)(input.uv.x * w), w - 1);
                uint row = min((uint)(input.uv.y * (h + h / 2)), h + h / 2 - 1);
                bool hdr10 = _Hdr > 0.5 && _Hdr < 1.5;
                float3 c;
                bool v = false, luma = row < h;
                if (luma) c = Coded(float2((col + 0.5) / w, (row + 0.5) / h), _Hdr);
                else
                {
                    uint quarter = (w / 2) * (h / 2);
                    uint i = (row - h) * w + col;
                    v = i >= quarter;
                    if (v) i -= quarter;
                    uint cx = i % (w / 2), cy = i / (w / 2);
                    c = Coded(float2((cx * 2 + 1.0) / w, (cy * 2 + 1.0) / h), _Hdr);   // between the 2 x 2 pixels: their average
                }
                if (!hdr10)
                {
                    // BT.709, 8 bits
                    if (luma) return (16.0 + dot(c, float3(46.559, 156.629, 15.812))) / 255.0;
                    float value = v ? dot(c, float3(112.0, -101.730, -10.270)) : dot(c, float3(-25.664, -86.336, 112.0));
                    return (128.0 + value) / 255.0;
                }
                // BT.2020 non-constant luminance, 10 bits, in the 16-bit sample's high bits
                float y = dot(c, float3(0.2627, 0.6780, 0.0593));
                float code = luma ? 64.0 + 876.0 * y : 512.0 + 896.0 * (v ? (c.r - y) / 1.4746 : (c.b - y) / 1.8814);
                return round(clamp(code, 0.0, 1023.0)) * 64.0 / 65535.0;
            }

            /// Pass 1, the thumbnail: the picture as SDR colour (an HDR source tone-mapped as _Hdr 2), rows the Unity way.
            float4 FragThumbnail(Varyings input) : SV_Target
            {
                return float4(Coded(float2(input.uv.x, 1.0 - input.uv.y), _Hdr > 0.5 ? 2.0 : 0.0), 1.0);
            }
        ENDHLSL

        Pass
        {
            Name "Pack"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "Thumbnail"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragThumbnail
            ENDHLSL
        }
    }
}
