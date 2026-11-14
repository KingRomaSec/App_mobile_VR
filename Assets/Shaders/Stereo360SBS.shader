// Shader vidéo VR multi-format.
//  _Projection : 0 = 360° équirectangulaire, 1 = 180° équirectangulaire, 2 = écran plat (UV du mesh)
//  _Layout     : 0 = mono, 1 = 3D côte à côte (œil gauche à gauche), 2 = 3D haut/bas (œil gauche en haut)
// Pour 360/180 la direction est calculée depuis la position du sommet (indépendant des UV de la sphère),
// avant de la vue = +Z de l'objet.
Shader "VR/Stereo360_SideBySide"
{
    Properties
    {
        _MainTex ("Texture Vidéo", 2D) = "black" {}
        [Enum(Equirect360,0,Equirect180,1,Flat,2)] _Projection ("Projection", Float) = 0
        [Enum(Mono,0,SideBySide,1,TopBottom,2)] _Layout ("Disposition stéréo", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
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
                float3 dir : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float _Projection;
            float _Layout;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 uv;
                if (_Projection < 1.5)
                {
                    float3 d = normalize(i.dir);
                    float lon = atan2(d.x, d.z);              // 0 = devant, positif à droite
                    float lat = asin(clamp(d.y, -1.0, 1.0));  // positif vers le haut

                    if (_Projection > 0.5)
                    {
                        // 180° : seulement l'hémisphère avant
                        if (abs(lon) > UNITY_PI * 0.5) return fixed4(0, 0, 0, 1);
                        uv.x = lon / UNITY_PI + 0.5;
                    }
                    else
                    {
                        uv.x = lon / (2.0 * UNITY_PI) + 0.5;
                    }
                    uv.y = lat / UNITY_PI + 0.5;
                }
                else
                {
                    uv = i.uv;
                }

                // 0 = œil gauche, 1 = œil droit (toujours 0 hors VR)
                float eye = unity_StereoEyeIndex;

                if (_Layout > 1.5)
                {
                    // Haut/bas : œil gauche = moitié haute
                    uv.y = uv.y * 0.5 + (1.0 - eye) * 0.5;
                }
                else if (_Layout > 0.5)
                {
                    // Côte à côte : œil gauche = moitié gauche
                    uv.x = uv.x * 0.5 + eye * 0.5;
                }

                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
