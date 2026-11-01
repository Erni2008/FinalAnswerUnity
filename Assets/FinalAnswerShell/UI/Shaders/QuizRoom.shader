// Port of QuizBackground's GLSL: localized flickering candle/chandelier light and a soft vignette over the room art.
Shader "FinalAnswer/Quiz Room"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _RoomTime ("Time", Float) = 0
        _Motion ("Motion", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float _RoomTime;
            float _Motion;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            // libGDX UVs grow downwards; Unity's grow upwards.
            float glow(float2 uv, float2 position, float2 size)
            {
                float2 d = (uv - position) / size;
                return exp(-dot(d, d) * 2.0);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 scene = tex2D(_MainTex, i.uv);
                float2 uv = float2(i.uv.x, 1.0 - i.uv.y);
                float t = _RoomTime;
                float left = sin(t * 1.27 + sin(t * .41) * .6);
                float right = sin(t * 1.09 + 1.8 + sin(t * .37) * .5);
                float chandelier = sin(t * .83 + .7);
                float light = glow(uv, float2(.354, .35), float2(.10, .22)) * left
                            + glow(uv, float2(.647, .35), float2(.10, .22)) * right
                            + glow(uv, float2(.50, .075), float2(.20, .15)) * chandelier * .7
                            + glow(uv, float2(.21, .55), float2(.09, .12)) * right * .4
                            + glow(uv, float2(.97, .56), float2(.09, .15)) * left * .4;
                // Gamma-space constants from the original, converted so the added light matches in a Linear project.
                float3 color = scene.rgb + GammaToLinearSpace(float3(.045, .023, .008)) * light * _Motion * 4.0;
                float vignette = smoothstep(.28, .73, length((uv - .5) * float2(1.0, .85)));
                color *= 1.0 - .19 * vignette;
                return fixed4(max(color, 0), scene.a) * i.color;
            }
            ENDCG
        }
    }
}
