// PvP opponent outline: an enlarged copy of each body part drawn back faces only, in a flat colour, so a coloured
// rim shows around the silhouette. Depth-tested like any mesh: it never shows through walls.
Shader "Hidden/RiftWard/Outline"
{
    Properties { _Color ("Color", Color) = (1, 0.18, 0.15, 1) }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            Cull Front
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
