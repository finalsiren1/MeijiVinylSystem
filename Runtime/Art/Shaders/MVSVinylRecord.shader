// Procedural vinyl record: black grooved disc with the album cover printed on the center label.
// Expects planar UVs over the record face (0..1, center at 0.5) - see the MVS_VinylDisc mesh.
// The cover comes from the library cover atlas, set per record with a MaterialPropertyBlock:
// _CoverTex = atlas page, _CoverRect = (x, y, width, height) UV rect of the cover; width <= 0 = blank label.
Shader "MeijiVinylSystem/Vinyl Record"
{
    Properties
    {
        [NoScaleOffset] _CoverTex ("Cover Atlas Page", 2D) = "white" {}
        _CoverRect ("Cover UV Rect (x, y, w, h)", Vector) = (0, 0, 0, 0)
        _LabelRadius ("Label Radius (0-1 of record)", Range(0.1, 0.9)) = 0.36
        _HoleRadius ("Spindle Hole Radius", Range(0, 0.1)) = 0.025
        _VinylColor ("Vinyl Color", Color) = (0.035, 0.035, 0.04, 1)
        _VinylSmoothness ("Vinyl Smoothness", Range(0, 1)) = 0.82
        _GrooveDensity ("Groove Density", Float) = 260
        _GrooveStrength ("Groove Strength", Range(0, 1)) = 0.35
        _RimWidth ("Plain Rim / Run-out Width", Range(0, 0.2)) = 0.04
        _LabelSmoothness ("Label Smoothness", Range(0, 1)) = 0.25
        [NoScaleOffset] _BlankLabel ("Blank Label (no cover)", 2D) = "gray" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing

        sampler2D _CoverTex;
        sampler2D _BlankLabel;
        float4 _CoverRect;
        half _LabelRadius, _HoleRadius, _VinylSmoothness, _GrooveDensity, _GrooveStrength, _RimWidth, _LabelSmoothness;
        fixed4 _VinylColor;

        struct Input { float2 uv_CoverTex; };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.uv_CoverTex * 2.0 - 1.0; // -1..1
            float r = length(p);

            // Label: map the label circle to the full cover texture.
            float2 labelUV = p / (_LabelRadius * 2.0) + 0.5;
            bool hasCover = _CoverRect.z > 0;
            float2 atlasUV = _CoverRect.xy + saturate(labelUV) * _CoverRect.zw;
            fixed3 label = hasCover ? tex2D(_CoverTex, atlasUV).rgb : tex2D(_BlankLabel, labelUV).rgb;

            // Grooves: fine concentric rings + a couple of wider track gaps.
            float grooves = 0.5 + 0.5 * sin(r * _GrooveDensity * 6.2831853);
            float gaps = smoothstep(0.0, 0.004, abs(frac(r * 5.0) - 0.5) - 0.49);
            float grooveMask = step(_LabelRadius + 0.02, r) * step(r, 1.0 - _RimWidth);
            fixed3 vinyl = _VinylColor.rgb * (1.0 + (grooves - 0.5) * _GrooveStrength * grooveMask) * (1.0 - 0.4 * gaps * grooveMask);

            float inLabel = step(r, _LabelRadius);
            float inHole = step(r, _HoleRadius);
            o.Albedo = lerp(vinyl, label, inLabel) * (1.0 - inHole);
            o.Smoothness = lerp(_VinylSmoothness * (0.85 + 0.15 * grooves), _LabelSmoothness, inLabel) * (1.0 - inHole);
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
