// The original's boost blur (post effect 0x1400002, Engine::SetPostEffect from MGame::OnUpdate while PlayerEgo::boosting
// and not docking to a planet; RadialBlurPass): the screen smeared toward the player's ship on screen (u_radial_origin, its
// screen position with y from the bottom), more the farther from it (mix by clamp(distance x u_blur_strength), the
// strength the boost percentage): the pixel plus 5 samples along the line to the origin, 0.01 to 0.03 of the screen
// either side, summed x 0.1429 (the 6th sample of the array is never read). Remake, 2026-11-17 (the user's request after
// the PC version's shaders: it ran on the phone too, the notes had it "never switched on"); always on.
Shader "Hidden/GoF2/RadialBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "RadialBlur"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float2 _RadialOrigin;   // viewport position of the ship (0..1, y up)
            float _BlurStrength;

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 dir = _RadialOrigin - uv;
                float dist = length(dir);
                dir /= max(dist, 1e-5);
                float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float4 sum = color;
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir * -0.03);
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir * -0.02);
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir * -0.01);
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir * 0.01);
                sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + dir * 0.02);
                sum *= 0.1429;
                return lerp(color, sum, saturate(dist * _BlurStrength));
            }
            ENDHLSL
        }
    }
}
