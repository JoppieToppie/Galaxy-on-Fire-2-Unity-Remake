// The photo tools' motion blur (PhotoMotionPass, Visuals): the Camera settings panel's Shutter angle. The world is frozen,
// so nothing moves between frames: each pixel's motion is worked out from the depth and what the pass was told moves,
// the ships' velocities from before the freeze (spheres around them, _Spheres / _SphereVel) and the camera's (_CamVel,
// the ship's in the orbit, none for the free camera); everything else stands still. Over the exposure (_Exposure s, the
// shutter centred on the frame) a point travels (v - camera v) x _Exposure. A gather in the manner of McGuire et al.,
// "A Reconstruction Filter for Plausible Motion Blur" (2012):
//   velocity    full size: the screen half-motion in pixels (xy), the eye depth (z)
//   tile max    a tile's longest half-motion (_Tile pixels a side)
//   neighbour   the longest of the 3 x 3 tiles around
//   gather      32 samples along the neighbourhood's motion, weighted so a sharp foreground (the ship in the orbit) keeps
//               its edge while what streaks behind it doesn't smear it, and a streaking ship smears over what lies behind
Shader "Hidden/GoF2/PhotoMotion"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

        #define MAX_SPHERES 32
        #define SAMPLES 32

        TEXTURE2D_X_FLOAT(_DepthTex);
        TEXTURE2D_X_FLOAT(_VelocityTex);
        TEXTURE2D_X_FLOAT(_NeighbourTex);
        float4 _Spheres[MAX_SPHERES];     // xyz world centre, w radius (m)
        float4 _SphereVel[MAX_SPHERES];   // xyz world velocity (m/s)
        int _SphereCount;
        float4 _CamVel;                   // xyz the camera's velocity (m/s)
        float _Exposure;                  // s
        float4 _Size;                     // the scene's width, height, the tiles' columns, rows
        float _Tile;                      // pixels a tile side, also the longest half-motion
        float _TileStride;                // the tile max reads every n-th pixel

        // ---- velocity -----------------------------------------------------------------------------------------------

        float4 FragVelocity(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float raw = SAMPLE_TEXTURE2D_X(_DepthTex, sampler_PointClamp, uv).r;
            float eye = LinearEyeDepth(raw, _ZBufferParams);
        #if UNITY_REVERSED_Z
            float device = raw;
        #else
            float device = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
        #endif
            float3 ws = ComputeWorldSpacePosition(uv, device, UNITY_MATRIX_I_VP);
            float3 v = 0;
            float best = 1e30;
            for (int i = 0; i < _SphereCount; i++)
            {
                float d = distance(ws, _Spheres[i].xyz);
                if (d < _Spheres[i].w && d < best) { best = d; v = _SphereVel[i].xyz; }
            }
            float3 travel = (v - _CamVel.xyz) * (_Exposure * 0.5);
            float4 a = mul(UNITY_MATRIX_VP, float4(ws - travel, 1.0));
            float4 b = mul(UNITY_MATRIX_VP, float4(ws + travel, 1.0));
            // A long exposure can carry a near point behind the camera: halve its travel until both ends are in front.
            [unroll] for (int k = 0; k < 4 && (a.w <= 0.001 || b.w <= 0.001); k++)
            {
                travel *= 0.5;
                a = mul(UNITY_MATRIX_VP, float4(ws - travel, 1.0));
                b = mul(UNITY_MATRIX_VP, float4(ws + travel, 1.0));
            }
            float2 motion = 0;
            if (a.w > 0.001 && b.w > 0.001)   // both ends in front of the camera
            {
                motion = (b.xy / b.w - a.xy / a.w) * 0.25 * _Size.xy;   // clip -1..1 to pixels, half of it
            #if UNITY_UV_STARTS_AT_TOP
                motion.y = -motion.y;
            #endif
                float len = length(motion);
                if (len > _Tile) motion *= _Tile / len;
            }
            return float4(motion, min(eye, 60000.0), 0);
        }

        // ---- tile max / neighbour max -------------------------------------------------------------------------------

        float2 Longer(float2 a, float2 b) { return dot(b, b) > dot(a, a) ? b : a; }

        float4 FragTileMax(Varyings input) : SV_Target
        {
            int2 tile = (int2)floor(input.texcoord * _Size.zw);
            int2 origin = tile * (int)_Tile;
            int2 last = (int2)_Size.xy - 1;
            int stride = max(1, (int)_TileStride);
            float2 m = 0;
            for (int y = 0; y < (int)_Tile; y += stride)
                for (int x = 0; x < (int)_Tile; x += stride)
                    m = Longer(m, LOAD_TEXTURE2D_X(_BlitTexture, min(origin + int2(x, y), last)).xy);
            return float4(m, 0, 0);
        }

        float4 FragNeighbour(Varyings input) : SV_Target
        {
            int2 tile = (int2)floor(input.texcoord * _Size.zw);
            int2 last = (int2)_Size.zw - 1;
            float2 m = 0;
            [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                    m = Longer(m, LOAD_TEXTURE2D_X(_BlitTexture, clamp(tile + int2(x, y), 0, last)).xy);
            return float4(m, 0, 0);
        }

        // ---- gather -------------------------------------------------------------------------------------------------

        float Cone(float dist, float len) { return saturate(1.0 - dist / len); }
        float Cylinder(float dist, float len) { return 1.0 - smoothstep(0.95 * len, 1.05 * len, dist); }
        // 1 when 'za' is in front of 'zb' (eye depths), fading over a few percent of the depth.
        float Front(float za, float zb) { return saturate(1.0 - (za - zb) / (0.03 * min(za, zb) + 0.25)); }

        float4 FragGather(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord;
            float4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
            float2 px = uv * _Size.xy;
            int2 tile = clamp((int2)floor(px / _Tile), 0, (int2)_Size.zw - 1);
            float2 vn = LOAD_TEXTURE2D_X(_NeighbourTex, tile).xy;
            float lenN = length(vn);
            if (lenN < 0.5) return scene;

            float4 vx = LOAD_TEXTURE2D_X(_VelocityTex, min((int2)px, (int2)_Size.xy - 1));
            float lenX = max(length(vx.xy), 0.5);
            float zX = vx.z;
            float weight = 1.0 / lenX;
            float3 sum = scene.rgb * weight;
            float jitter = InterleavedGradientNoise(px, 0) - 0.5;
            [loop] for (int i = 0; i < SAMPLES; i++)
            {
                float t = lerp(-1.0, 1.0, (i + 0.5 + jitter) / SAMPLES);
                float2 at = px + vn * t;
                if (any(at < 0.0) || any(at >= _Size.xy)) continue;
                float4 vy = LOAD_TEXTURE2D_X(_VelocityTex, (int2)at);
                float lenY = max(length(vy.xy), 0.5);
                float dist = abs(t) * lenN;
                float zY = vy.z;
                float alpha = Front(zY, zX) * Cone(dist, lenY)           // a streaking Y in front of X
                            + Front(zX, zY) * Cone(dist, lenX)           // what lies behind a streaking X
                            + Cylinder(dist, lenY) * Cylinder(dist, lenX) * 2.0;
                weight += alpha;
                sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, at / _Size.xy, 0).rgb * alpha;
            }
            return float4(sum / weight, scene.a);
        }
        ENDHLSL

        Pass
        {
            Name "Velocity"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVelocity
            ENDHLSL
        }
        Pass
        {
            Name "TileMax"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTileMax
            ENDHLSL
        }
        Pass
        {
            Name "Neighbour"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragNeighbour
            ENDHLSL
        }
        Pass
        {
            Name "Gather"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGather
            ENDHLSL
        }
    }
}
