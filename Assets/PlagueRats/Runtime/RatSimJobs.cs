using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PlagueRats
{
    public enum RatState : byte { Idle = 0, Run = 1, Flee = 2, Attack = 3 }

    /// <summary>A round torch/light the rats flee from.</summary>
    public struct LightPoint
    {
        public float2 pos;
        public float radius;
        public float strength;
    }

    /// <summary>A directional spot-light cone the rats flee from (XZ projection).</summary>
    public struct SpotCone
    {
        public float2 pos;        // apex (XZ)
        public float2 forward;    // normalized facing (XZ)
        public float range;       // cone length
        public float cosHalf;     // cos(halfAngle) for the dot test
        public float strength;    // push force
    }

    [BurstCompile]
    public struct RatSimJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> positionsRead;
        [ReadOnly] public NativeArray<float2> zoneMin;
        [ReadOnly] public NativeArray<float2> zoneMax;
        [ReadOnly] public NativeArray<LightPoint> lights;
        [ReadOnly] public NativeArray<SpotCone> spots;

        public float dt, time;
        public float maxSpeed, accel;
        public float sepRadius, wSep, wWander;
        public float neighborSampleRadius;

        public float2 targetPos;
        public byte hasTarget;
        public byte anyLightOn;
        public float attackRadius;
        public float attackFraction;

        [WriteOnly] public NativeArray<float2> positions;
        public NativeArray<float2> velocities;
        [WriteOnly] public NativeArray<RatState> states;

        public void Execute(int i)
        {
            float2 pos = positionsRead[i];
            float2 desired = float2.zero;

            float2 mn = zoneMin[i], mx = zoneMax[i];
            bool targetInMyZone =
                targetPos.x >= mn.x && targetPos.x <= mx.x &&
                targetPos.y >= mn.y && targetPos.y <= mx.y;

            float attackRoll = math.frac(math.sin(i * 12.9898f) * 43758.5453f);
            bool isAttacker = attackRoll < attackFraction;

            bool swarming = (anyLightOn == 0) && (hasTarget == 1)
                            && targetInMyZone && isAttacker;

            // separation
            float sepR2 = sepRadius * sepRadius;
            float2 sep = float2.zero;
            int checkStart = math.max(0, i - 64);
            int checkEnd = math.min(positionsRead.Length, i + 64);
            for (int j = checkStart; j < checkEnd; j++)
            {
                if (j == i) continue;
                float2 diff = pos - positionsRead[j];
                float d2 = math.lengthsq(diff);
                if (d2 < sepR2 && d2 > 1e-6f) sep += diff / d2;
            }
            desired += math.normalizesafe(sep) * wSep;

            bool fleeing = false;
            bool attacking = false;

            if (swarming)
            {
                float2 toTarget = targetPos - pos;
                float dist = math.length(toTarget);
                if (dist > 1e-4f) desired += (toTarget / dist) * 2.5f;
                if (dist < attackRadius) attacking = true;
            }
            else
            {
                float idHash = math.frac(math.sin(i * 91.137f) * 9173.21f);
                float turnRate = 0.25f + idHash * 0.75f;
                float angle = i * 2.3998f + time * turnRate + math.sin(time * 0.5f + i) * 1.5f;
                desired += new float2(math.cos(angle), math.sin(angle)) * wWander;

                float2 zoneCenter = (mn + mx) * 0.5f;
                float2 toCenter = zoneCenter - pos;
                float dCenter = math.length(toCenter);
                if (dCenter > 1e-3f) desired += (toCenter / dCenter) * 0.15f;

                // --- flee round lights (torch) ---
                for (int l = 0; l < lights.Length; l++)
                {
                    var L = lights[l];
                    if (L.strength <= 0f) continue;
                    float2 away = pos - L.pos;
                    float d = math.length(away);
                    if (d < L.radius && d > 1e-4f)
                    {
                        float push = (1f - d / L.radius) * L.strength;
                        desired += (away / d) * push;
                        fleeing = true;
                    }
                }

                // --- flee spot-light cones (search light) ---
                for (int s = 0; s < spots.Length; s++)
                {
                    var C = spots[s];
                    if (C.strength <= 0f || C.range <= 0f) continue;
                    float2 toRat = pos - C.pos;
                    float d = math.length(toRat);
                    if (d < 1e-4f || d > C.range) continue;
                    float2 dirToRat = toRat / d;
                    // inside the cone if the rat is within the half-angle of forward
                    if (math.dot(dirToRat, C.forward) >= C.cosHalf)
                    {
                        float push = (1f - d / C.range) * C.strength;
                        desired += dirToRat * push;   // push straight away from apex
                        fleeing = true;
                    }
                }
            }

            float speedVar = 0.6f + math.frac(math.sin(i * 45.13f) * 7841.31f);
            float2 dirN = math.normalizesafe(desired);
            if (math.lengthsq(dirN) < 1e-6f)
            {
                float a = i * 2.3998f + time;
                dirN = new float2(math.cos(a), math.sin(a));
            }
            float2 targetVel = dirN * maxSpeed * speedVar;
            float2 vel = MoveTowards(velocities[i], targetVel, accel * dt);
            float2 next = pos + vel * dt;

            if (!swarming)
            {
                if (next.x < mn.x) { next.x = mn.x; vel.x = math.abs(vel.x); }
                else if (next.x > mx.x) { next.x = mx.x; vel.x = -math.abs(vel.x); }
                if (next.y < mn.y) { next.y = mn.y; vel.y = math.abs(vel.y); }
                else if (next.y > mx.y) { next.y = mx.y; vel.y = -math.abs(vel.y); }
            }

            positions[i] = next;
            velocities[i] = vel;

            float spd2 = math.lengthsq(vel);
            states[i] = attacking ? RatState.Attack
                      : fleeing ? RatState.Flee
                      : (spd2 > 0.6f ? RatState.Run : RatState.Idle);
        }

        static float2 MoveTowards(float2 cur, float2 tgt, float maxDelta)
        {
            float2 d = tgt - cur;
            float m = math.length(d);
            return (m <= maxDelta || m < 1e-6f) ? tgt : cur + d / m * maxDelta;
        }
    }

    [BurstCompile]
    public struct BuildMatricesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> positions;
        [ReadOnly] public NativeArray<float2> velocities;
        [ReadOnly] public NativeArray<float> groundY;
        public float scaleMin, scaleMax;
        [WriteOnly] public NativeArray<Matrix4x4> matrices;

        public void Execute(int i)
        {
            float3 pos = new float3(positions[i].x, groundY[i], positions[i].y);
            float2 v = velocities[i];
            quaternion rot = math.lengthsq(v) > 1e-4f
                ? quaternion.RotateY(math.atan2(v.x, v.y))
                : quaternion.identity;
            float t = math.frac(math.sin(i * 78.233f) * 43758.5453f);
            float s = math.lerp(scaleMin, scaleMax, t);
            matrices[i] = Matrix4x4.TRS(pos, rot, new float3(s, s, s));
        }
    }
}