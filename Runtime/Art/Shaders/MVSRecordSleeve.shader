// Record sleeve (album jacket): front cover on the top face, back cover on the bottom face, plain card edges.
// Expects the MVS_RecordSleeve mesh: UV0 = 0..1 across each face, UV1.x = face id (0 front, 1 back, 2 edge).
// Front/back come from the library atlases, set per sleeve with a MaterialPropertyBlock:
// _CoverTex/_CoverRect (front), _BackTex/_BackRect (back). A rect width <= 0 means "no art" (plain card).
Shader "MeijiVinylSystem/Record Sleeve"
{
    Properties
    {
        [NoScaleOffset] _CoverTex ("Front Atlas Page", 2D) = "white" {}
        _CoverRect ("Front UV Rect (x, y, w, h)", Vector) = (0, 0, 0, 0)
        [NoScaleOffset] _BackTex ("Back Atlas Page", 2D) = "white" {}
        _BackRect ("Back UV Rect (x, y, w, h)", Vector) = (0, 0, 0, 0)
        _CardColor ("Card / Edge Color", Color) = (0.82, 0.8, 0.76, 1)
        _BlankColor ("No Art Color", Color) = (0.18, 0.18, 0.2, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _EdgeWear ("Edge Wear", Range(0, 0.1)) = 0.012
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing

        sampler2D _CoverTex, _BackTex;
        float4 _CoverRect, _BackRect;
        fixed4 _CardColor, _BlankColor;
        half _Smoothness, _EdgeWear;

        struct Input
        {
            float2 uv_CoverTex;
            float2 uv2_BackTex;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float face = IN.uv2_BackTex.x;
            float2 uv = saturate(IN.uv_CoverTex);
            fixed3 col;
            if (face > 1.5)
            {
                col = _CardColor.rgb;
            }
            else
            {
                bool back = face > 0.5;
                float4 r = back ? _BackRect : _CoverRect;
                if (r.z > 0)
                    col = back ? tex2D(_BackTex, r.xy + uv * r.zw).rgb : tex2D(_CoverTex, r.xy + uv * r.zw).rgb;
                else
                    col = _BlankColor.rgb;
                // slightly worn, lighter edge of the printed card
                float2 d = min(uv, 1 - uv);
                float wear = 1 - smoothstep(0, _EdgeWear, min(d.x, d.y));
                col = lerp(col, _CardColor.rgb, wear * 0.6);
            }
            o.Albedo = col;
            o.Smoothness = _Smoothness;
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
