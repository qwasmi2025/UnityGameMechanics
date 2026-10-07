// GPU Instancer Pro
// Copyright (c) GurBu Technologies

#include "Packages/com.gurbu.gpui-pro/Runtime/Compute/Include/GPUIDefines.hlsl"

#ifndef _gpui_pro_crowd_defines_hlsl
#define _gpui_pro_crowd_defines_hlsl

struct GPUICrowdInstanceData
{
    int animatorWorkflowID;
    uint settings;
};

struct GPUICrowdAnimatorClipData
{
    int packedFrameStartAndBakedClipIndex;
    int packedFrameCountLoopAndSpeed;
    float clipStartTime;
    float packedSpeedRelativeLengthAndSync;
};

bool IsGPUAnimator(int workflowID)
{
    return workflowID < 100 || workflowID >= 200;
}

bool IsBakedAnimator(int workflowID)
{
    return workflowID < 100 || workflowID >= 200;
}

#ifndef GPUI_CROWD_IsGPUAnimator
#define GPUI_CROWD_IsGPUAnimator(workflowID) IsGPUAnimator(workflowID)
#endif
#ifndef GPUI_CROWD_IsBakedAnimator
#define GPUI_CROWD_IsBakedAnimator(workflowID) IsBakedAnimator(workflowID)
#endif

#endif