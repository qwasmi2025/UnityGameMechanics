// GPU Instancer Pro
// Copyright (c) GurBu Technologies

//#include_with_pragmas "Packages/com.gurbu.gpui-pro/Runtime/Shaders/Include/GPUInstancerSetup.hlsl"
#include "Packages/com.gurbu.gpui-pro.crowd-animations/Runtime/Shaders/Include/GPUICrowdInput.hlsl"

#ifndef GPUI_PRO_CROWD_SETUP_INCLUDED
#define GPUI_PRO_CROWD_SETUP_INCLUDED

#ifndef GPUI_PRO_CROWD_PRAGMAS_DEFINED
    #define GPUI_PRO_CROWD_PRAGMAS_DEFINED
    #pragma shader_feature_local _ GPUI_CROWD_SKIN_WEIGHTS_2 GPUI_CROWD_SKIN_WEIGHTS_1
#endif

#ifdef GPUI_PRO_CROWD_ACTIVE
    #define GPUI_CROWD_VERTEX(vertexID, vertex, normal, tangent) gpuiProCrowdSkinning(vertexID, vertex, normal, tangent)
    #define GPUI_CROWD_VERTEX_VN(vertexID, vertex, normal) gpuiProCrowdSkinning(vertexID, vertex, normal)
    #define GPUI_CROWD_VERTEX_V(vertexID, vertex) gpuiProCrowdSkinning(vertexID, vertex)
#else // GPUI_PRO_ACTIVE
    #define GPUI_CROWD_VERTEX(vertexID, vertex, normal, tangent)
    #define GPUI_CROWD_VERTEX_VN(vertexID, vertex, normal)
    #define GPUI_CROWD_VERTEX_V(vertexID, vertex)
#endif // GPUI_PRO_ACTIVE

void gpuiProCrowdAnimate_float(uint vertexID, float3 vertex, float3 normal, float3 tangent, out float3 out_vertex, out float3 out_normal, out float3 out_tangent)
{
    out_vertex = vertex;
    out_normal = normal;
    out_tangent = tangent;
    gpuiProCrowdSkinning(vertexID, out_vertex, out_normal, out_tangent);
}

#endif // GPUI_PRO_CROWD_SETUP_INCLUDED