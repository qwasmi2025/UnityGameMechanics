// GPU Instancer Pro
// Copyright (c) GurBu Technologies

#ifndef GPU_INSTANCER_PRO_INCLUDED
#define GPU_INSTANCER_PRO_INCLUDED

#ifndef GPUI_PRO_PRAGMAS_DEFINED
    #define GPUI_PRO_PRAGMAS_DEFINED
    #pragma shader_feature_local _ GPUI_OBJECT_MOTION_VECTOR_ON
    #pragma shader_feature_local _ GPUI_PER_INSTANCE_LIGHTPROBES_ON
    #pragma multi_compile _ GPUI_NO_BUFFER
#endif

#include "Packages/com.gurbu.gpui-pro/Runtime/Shaders/Include/GPUInstancerSetupNoPragma.hlsl"

#endif // GPU_INSTANCER_PRO_INCLUDED