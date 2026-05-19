// Ported from: src/game/bg_slidemove.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Network;

namespace ET.Game
{
    public partial class PlayerMovement
    {
        // -----------------------------------------------------------------------
        // PM_ClipVelocity
        //   Slide off of the impacting surface.
        //   Equivalent to PM_ClipVelocity() in bg_pmove.c / bg_slidemove.c.
        // -----------------------------------------------------------------------
        private static Vector3 PM_ClipVelocity(Vector3 inVel, Vector3 normal, float overbounce)
        {
            float backoff = Vector3.Dot(inVel, normal);
            if (backoff < 0f)
                backoff *= overbounce;
            else
                backoff /= overbounce;

            return inVel - normal * backoff;
        }

        // -----------------------------------------------------------------------
        // PM_SlideMove
        //   Returns true if the velocity was clipped in some way.
        //   Equivalent to PM_SlideMove() in bg_slidemove.c.
        // -----------------------------------------------------------------------
        private bool PM_SlideMove(bool gravity)
        {
            const int numbumps = 4;
            int extrabumps = 0;

            // Work with Vector3 wrappers around the flat ps fields
            Vector3 velocity  = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            Vector3 origin    = new Vector3(_pm.Ps.Origin0,   _pm.Ps.Origin1,   _pm.Ps.Origin2);

            Vector3 primalVelocity = velocity;
            Vector3 endVelocity    = Vector3.zero;

            if (gravity)
            {
                endVelocity    = velocity;
                endVelocity.z -= _pm.Ps.Gravity * _pml.FrameTime;
                velocity.z     = (velocity.z + endVelocity.z) * 0.5f;
                primalVelocity.z = endVelocity.z;

                if (_pml.GroundPlane)
                {
                    // slide along the ground plane
                    velocity = PM_ClipVelocity(velocity, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP);
                }
            }

            float timeLeft = _pml.FrameTime;

            // Seed planes array — never turn against the ground plane
            Vector3[] planes  = new Vector3[GameConst.MAX_CLIP_PLANES];
            int numplanes = 0;

            if (_pml.GroundPlane)
            {
                planes[numplanes++] = _pml.GroundTrace.PlaneNormal;
            }

            // Never turn against original velocity direction
            planes[numplanes++] = velocity.magnitude > 0f ? velocity.normalized : Vector3.zero;

            int bumpcount;
            for (bumpcount = 0; bumpcount < numbumps; bumpcount++)
            {
                // Calculate position we are trying to move to
                Vector3 end = origin + velocity * timeLeft;

                // See if we can make it there
                TraceResult trace = PM_TraceAll(origin, end);

                if (trace.AllSolid)
                {
                    // Entity is completely trapped in another solid
                    velocity.z = 0f;   // don't build up falling damage, allow sideways acceleration
                    WriteVelocity(velocity);
                    return true;
                }

                if (trace.Fraction > 0f)
                {
                    // Actually covered some distance
                    origin = trace.EndPos;
                }

                if (trace.Fraction == 1f)
                    break;  // moved the entire distance

                // Save entity for contact
                PM_AddTouchEnt(trace.EntityNum);

                timeLeft -= timeLeft * trace.Fraction;

                if (numplanes >= GameConst.MAX_CLIP_PLANES)
                {
                    // This shouldn't really happen
                    velocity = Vector3.zero;
                    WriteVelocity(velocity);
                    WriteOrigin(origin);
                    return true;
                }

                // If this is the same plane we hit before, nudge velocity
                // out along it (fixes epsilon issues with non-axial planes)
                bool hitExistingPlane = false;
                for (int i = 0; i < numplanes; i++)
                {
                    if (Vector3.Dot(trace.PlaneNormal, planes[i]) > 0.99f)
                    {
                        if (extrabumps <= 0)
                        {
                            velocity += trace.PlaneNormal;
                            extrabumps++;
                            // numbumps++ equivalent — we allow one extra iteration
                        }
                        else
                        {
                            // Nudge origin instead and re-trace
                            Vector3 nudgeEnd = origin + trace.PlaneNormal;
                            TraceResult nudgeTr = PM_TraceAll(origin, nudgeEnd);
                            origin = nudgeTr.EndPos;
                        }
                        hitExistingPlane = true;
                        break;
                    }
                }
                if (hitExistingPlane)
                    continue;

                planes[numplanes++] = trace.PlaneNormal;

                // Modify velocity so it parallels all clip planes
                // Find a plane that it enters
                for (int i = 0; i < numplanes; i++)
                {
                    float into = Vector3.Dot(velocity, planes[i]);
                    if (into >= 0.1f)
                        continue;   // move doesn't interact with the plane

                    // See how hard we are hitting things
                    if (-into > _pml.ImpactSpeed)
                        _pml.ImpactSpeed = -into;

                    // Slide along the plane
                    Vector3 clipVelocity    = PM_ClipVelocity(velocity,    planes[i], GameConst.OVERCLIP);
                    Vector3 endClipVelocity = PM_ClipVelocity(endVelocity, planes[i], GameConst.OVERCLIP);

                    // See if there is a second plane the new move enters
                    for (int j = 0; j < numplanes; j++)
                    {
                        if (j == i) continue;
                        if (Vector3.Dot(clipVelocity, planes[j]) >= 0.1f)
                            continue;   // move doesn't interact with the plane

                        // Try clipping the move to the plane
                        clipVelocity    = PM_ClipVelocity(clipVelocity,    planes[j], GameConst.OVERCLIP);
                        endClipVelocity = PM_ClipVelocity(endClipVelocity, planes[j], GameConst.OVERCLIP);

                        // See if it goes back into the first clip plane
                        if (Vector3.Dot(clipVelocity, planes[i]) >= 0f)
                            continue;

                        // Slide the original velocity along the crease
                        Vector3 dir = Vector3.Cross(planes[i], planes[j]).normalized;
                        float d = Vector3.Dot(dir, velocity);
                        clipVelocity = dir * d;

                        dir = Vector3.Cross(planes[i], planes[j]).normalized;
                        d   = Vector3.Dot(dir, endVelocity);
                        endClipVelocity = dir * d;

                        // See if there is a third plane the new move enters
                        bool tripleBlock = false;
                        for (int k = 0; k < numplanes; k++)
                        {
                            if (k == i || k == j) continue;
                            if (Vector3.Dot(clipVelocity, planes[k]) >= 0.1f)
                                continue;   // move doesn't interact with the plane

                            // Stop dead at a triple plane interaction
                            velocity = Vector3.zero;
                            WriteVelocity(velocity);
                            WriteOrigin(origin);
                            return true;
                        }
                        if (tripleBlock) break;
                    }

                    // If we have fixed all interactions, try another move
                    velocity    = clipVelocity;
                    endVelocity = endClipVelocity;
                    break;
                }
            }

            if (gravity)
                velocity = endVelocity;

            // Don't change velocity if in a timer
            if (_pm.Ps.PmTime != 0)
                velocity = primalVelocity;

            WriteOrigin(origin);
            WriteVelocity(velocity);

            return (bumpcount != 0);
        }

        // -----------------------------------------------------------------------
        // PM_StepSlideMove
        //   Equivalent to PM_StepSlideMove() in bg_slidemove.c.
        // -----------------------------------------------------------------------
        private void PM_StepSlideMove(bool gravity)
        {
            Vector3 startO = new Vector3(_pm.Ps.Origin0,   _pm.Ps.Origin1,   _pm.Ps.Origin2);
            Vector3 startV = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);

            if (PM_SlideMove(gravity) == false)
                return; // we got exactly where we wanted to go first try

            // Check if we should attempt a step up
            Vector3 down = startO;
            down.z -= GameConst.STEPSIZE;

            TraceResult trace = PM_TraceAll(startO, down);
            Vector3 up = new Vector3(0f, 0f, 1f);

            // Never step up when you still have upward velocity
            float velZ = _pm.Ps.Velocity2; // may have been modified by SlideMove
            if (velZ > 0f && (trace.Fraction == 1.0f || Vector3.Dot(trace.PlaneNormal, up) < 0.7f))
                return;

            // Save the down-slid position
            Vector3 downO = new Vector3(_pm.Ps.Origin0,   _pm.Ps.Origin1,   _pm.Ps.Origin2);
            Vector3 downV = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);

            // Try moving STEPSIZE higher
            Vector3 upPos = startO;
            upPos.z += GameConst.STEPSIZE;

            trace = PM_TraceAll(upPos, upPos);
            if (trace.AllSolid)
                return; // can't step up

            // Try slide move from the raised position
            WriteOrigin(upPos);
            WriteVelocity(startV);

            PM_SlideMove(gravity);

            // Push down the final amount
            Vector3 newOrigin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
            down    = newOrigin;
            down.z -= GameConst.STEPSIZE;

            // Check legs separately when prone
            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                TraceResult legTrace = PM_TraceLegs(
                    newOrigin, down,
                    new Vector3(_pm.Ps.ViewAngles0, _pm.Ps.ViewAngles1, _pm.Ps.ViewAngles2),
                    _pm.Ps.ClientNum, _pm.TraceMask);

                if (legTrace.AllSolid)
                {
                    // Legs don't step — revert
                    WriteOrigin(downO);
                    WriteVelocity(downV);
                    return;
                }
            }

            trace = _pm.Trace(
                newOrigin,
                _pm.Mins,
                _pm.Maxs,
                down,
                _pm.Ps.ClientNum,
                _pm.TraceMask);

            if (!trace.AllSolid)
                WriteOrigin(trace.EndPos);

            if (trace.Fraction < 1.0f)
            {
                Vector3 v = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
                v = PM_ClipVelocity(v, trace.PlaneNormal, GameConst.OVERCLIP);
                WriteVelocity(v);
            }

            // Emit step event based on delta height
            {
                float delta = _pm.Ps.Origin2 - startO.z;
                if (delta > 2f)
                {
                    if (delta < 7f)
                        AddEvent(EV_STEP_4);
                    else if (delta < 11f)
                        AddEvent(EV_STEP_8);
                    else if (delta < 15f)
                        AddEvent(EV_STEP_12);
                    else
                        AddEvent(EV_STEP_16);
                }
            }
        }

        // -----------------------------------------------------------------------
        // Helpers: read/write Vector3 ↔ flattened ps fields
        // -----------------------------------------------------------------------
        private void WriteOrigin(Vector3 v)
        {
            _pm.Ps.Origin0 = v.x;
            _pm.Ps.Origin1 = v.y;
            _pm.Ps.Origin2 = v.z;
        }

        private void WriteVelocity(Vector3 v)
        {
            _pm.Ps.Velocity0 = v.x;
            _pm.Ps.Velocity1 = v.y;
            _pm.Ps.Velocity2 = v.z;
        }
    }
}
