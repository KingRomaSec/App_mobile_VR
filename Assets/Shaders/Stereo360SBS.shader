Shader "VR/Stereo360_SideBySide"
{
    Properties
    {
        _MainTex ("Texture Video 360", 2D) = "black" {}
        [Toggle] _InvertX ("Inverser Horizontal (Vue Intérieure)", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        Cull Front // Affiche l'intérieur de la sphère Unity standard
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _InvertX;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv = i.uv;

                // Vue de l'intérieur de la sphère : inversion horizontale pour orientation naturelle
                if (_InvertX > 0.5)
                {
                    uv.x = 1.0 - uv.x;
                }

                // Détection de l'œil (0 = Œil Gauche, 1 = Œil Droit)
                float eyeIndex = unity_StereoEyeIndex;

                // Rendu Side-by-Side (SBS) :
                // Œil gauche  -> échantillonne la moitié gauche  [0.0 - 0.5]
                // Œil droit   -> échantillonne la moitié droite [0.5 - 1.0]
                uv.x = (uv.x * 0.5) + (eyeIndex * 0.5);

                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
