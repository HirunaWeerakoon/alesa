Shader "UI/AnalogPauseFrame"
{
    Properties { _MainTex("Frame", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            sampler2D _MainTex;
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed3 rgb=tex2D(_MainTex,i.uv).rgb;
                float grey=dot(rgb,float3(0.299,0.587,0.114));
                float2 edge=(i.uv-0.5)*2;
                float vignette=1-saturate(dot(edge,edge)*0.45);
                return fixed4(lerp(rgb,grey.xxx,0.85)*vignette,i.color.a);
            }
            ENDCG
        }
    }
}
