Shader "Hidden/GPUInstancerPro/Nature/TreeProxy"
{
	SubShader
	{
		Tags 
		{ 
			"Queue"="Transparent" 
			"RenderType"="Transparent"
			//"ForceNoShadowCasting"="True"
			//"IgnoreProjector"="True"
			//"LightMode"="Always" 
		}
		LOD 100
		
		//ZTest Always
        Cull Off
        //ZWrite Off
        //Fog { Mode off }
        //Blend Off

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			struct appdata
			{
				float4 vertex : POSITION;
			};

			struct v2f
			{
				float4 pos : SV_POSITION;
			};

			v2f vert (appdata v) // needs position for cross platform compatibility
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				return o;
			}

			void frag ()
			{
				discard;
			}
			ENDCG
		}
	}
}

