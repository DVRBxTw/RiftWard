// Lightweight post stack: bloom (bright pass + dual-filter blur), ACES tonemap, grading, vignette.
Shader "Hidden/RiftWard/Post"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _Bloom;
    float4 _MainTex_TexelSize;
    float _Threshold, _Intensity, _Exposure, _Saturation, _Contrast, _Vignette;
    float3 _Tint;

    half3 Box(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-d, -d, d, d);
        return (tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb) * 0.25;
    }
    half4 Prefilter(v2f_img i) : SV_Target
    {
        half3 c = Box(i.uv, 1);
        half b = max(c.r, max(c.g, c.b));
        return half4(c * max(0, b - _Threshold) / max(b, 1e-4), 1);
    }
    half4 Down(v2f_img i) : SV_Target { return half4(Box(i.uv, 1), 1); }
    half4 Up(v2f_img i) : SV_Target { return half4(Box(i.uv, 0.5), 1); }
    half3 Aces(half3 x) { return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14)); }
    half4 Final(v2f_img i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb + tex2D(_Bloom, i.uv).rgb * _Intensity;
        // The project renders in gamma space: tonemap in (approximate) linear light, then return to gamma.
        c = sqrt(Aces(c * c * _Exposure)) * _Tint;
        half l = dot(c, half3(0.2126, 0.7152, 0.0722));
        c = lerp(l.xxx, c, _Saturation);
        c = saturate((c - 0.5) * _Contrast + 0.5);
        float2 d = i.uv - 0.5;
        c *= 1 - _Vignette * smoothstep(0.25, 0.85, dot(d, d) * 2.2);
        return half4(c, 1);
    }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Prefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Down
            ENDCG }
        Pass { Blend One One
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Up
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Final
            ENDCG }
    }
}
