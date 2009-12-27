﻿/*
  Box2DX Copyright (c) 2008 Ihar Kalasouski http://code.google.com/p/box2dx
  Box2D original C++ version Copyright (c) 2006-2007 Erin Catto http://www.gphysics.com

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.
*/

// 1-D constrained system
// m (v2 - v1) = lambda
// v2 + (beta/h) * x1 + gamma * lambda = 0, gamma has units of inverse mass.
// x2 = x1 + h * v2

// 1-D mass-damper-spring system
// m (v2 - v1) + h * d * v2 + h * k * 

// C = norm(p2 - p1) - L
// u = (p2 - p1) / norm(p2 - p1)
// Cdot = dot(u, v2 + cross(w2, r2) - v1 - cross(w1, r1))
// J = [-u -cross(r1, u) u cross(r2, u)]
// K = J * invM * JT
//   = invMass1 + invI1 * cross(r1, u)^2 + invMass2 + invI2 * cross(r2, u)^2

using Box2DX.Common;

namespace Box2DX.Dynamics
{
    /// <summary>
    /// Distance joint definition. This requires defining an
    /// anchor point on both bodies and the non-zero length of the
    /// distance joint. The definition uses local anchor points
    /// so that the initial configuration can violate the constraint
    /// slightly. This helps when saving and loading a game.
    /// @warning Do not use a zero or short length.
    /// </summary>
    public class DistanceJointDef : JointDef
    {
        public DistanceJointDef()
        {
            Type = JointType.DistanceJoint;
            LocalAnchorA.Set(0.0f, 0.0f);
            LocalAnchorB.Set(0.0f, 0.0f);
            Length = 1.0f;
            FrequencyHz = 0.0f;
            DampingRatio = 0.0f;
        }

        /// <summary>
        /// Initialize the bodies, anchors, and length using the world anchors.
        /// </summary>
        public void Initialize(Body body1, Body body2, Vec2 anchor1, Vec2 anchor2)
        {
            BodyA = body1;
            BodyB = body2;
            LocalAnchorA = body1.GetLocalPoint(anchor1);
            LocalAnchorB = body2.GetLocalPoint(anchor2);
            Vec2 d = anchor2 - anchor1;
            Length = d.Length();
        }

        /// <summary>
        /// The local anchor point relative to body1's origin.
        /// </summary>
        public Vec2 LocalAnchorA;

        /// <summary>
        /// The local anchor point relative to body2's origin.
        /// </summary>
        public Vec2 LocalAnchorB;

        /// <summary>
        /// The natural length between the anchor points.
        /// </summary>
        public float Length;

        /// <summary>
        /// The response speed.
        /// </summary>
        public float FrequencyHz;

        /// <summary>
        /// The damping ratio. 0 = no damping, 1 = critical damping.
        /// </summary>
        public float DampingRatio;
    }

    /// <summary>
    /// A distance joint constrains two points on two bodies
    /// to remain at a fixed distance from each other. You can view
    /// this as a massless, rigid rod.
    /// </summary>
    public class DistanceJoint : Joint
    {
        public Vec2 _localAnchorA;
        public Vec2 _localAnchorB;
        public Vec2 _u;
        public float _frequencyHz;
        public float _dampingRatio;
        public float _gamma;
        public float _bias;
        public float _impulse;
        public float _mass;
        public float _length;

        public override Vec2 GetAnchorA()
        {
           return _bodyA.GetWorldPoint(_localAnchorA); 
        }

        public override Vec2 GetAnchorB()
        {
           return _bodyB.GetWorldPoint(_localAnchorB); 
        }

        public override Vec2 GetReactionForce(float inv_dt)
        {
            return (inv_dt * _impulse) * _u;
        }

        public override float GetReactionTorque(float inv_dt)
        {
            //B2_NOT_USED(inv_dt);
            return 0.0f;
        }

        public DistanceJoint(DistanceJointDef def)
            : base(def)
        {
            _localAnchorA = def.LocalAnchorA;
            _localAnchorB = def.LocalAnchorB;
            _length = def.Length;
            _frequencyHz = def.FrequencyHz;
            _dampingRatio = def.DampingRatio;
            _impulse = 0.0f;
            _gamma = 0.0f;
            _bias = 0.0f;
        }

        internal override void InitVelocityConstraints(TimeStep step)
        {
            Body b1 = _bodyA;
            Body b2 = _bodyB;

            // Compute the effective mass matrix.
            Vec2 r1 = Math.Mul(b1.GetTransform().R, _localAnchorA - b1.GetLocalCenter());
            Vec2 r2 = Math.Mul(b2.GetTransform().R, _localAnchorB - b2.GetLocalCenter());
            _u = b2._sweep.C + r2 - b1._sweep.C - r1;

            // Handle singularity.
            float length = _u.Length();
            if (length > Settings.LinearSlop)
            {
                _u *= 1.0f / length;
            }
            else
            {
                _u.Set(0.0f, 0.0f);
            }

            float cr1u = Vec2.Cross(r1, _u);
            float cr2u = Vec2.Cross(r2, _u);
            float invMass = b1._invMass + b1._invI * cr1u * cr1u + b2._invMass + b2._invI * cr2u * cr2u;

            _mass = invMass != 0.0f ? 1.0f / invMass : 0.0f;

            if (_frequencyHz > 0.0f)
            {
                float C = length - _length;

                // Frequency
                float omega = 2.0f * Settings.pi * _frequencyHz;

                // Damping coefficient
                float d = 2.0f * _mass * _dampingRatio * omega;

                // Spring stiffness
                float k = _mass * omega * omega;

                // magic formulas
                _gamma = step.Dt * (d + step.Dt * k);
                _gamma = _gamma != 0.0f ? 1.0f / _gamma : 0.0f;
                _bias = C * step.Dt * k * _gamma;

                _mass = invMass + _gamma;
                _mass = _mass != 0.0f ? 1.0f / _mass : 0.0f;
            }

            if (step.WarmStarting)
            {
                //Scale the inpulse to support a variable timestep.
                _impulse *= step.DtRatio;

                Vec2 P = _impulse * _u;
                b1._linearVelocity -= b1._invMass * P;
                b1._angularVelocity -= b1._invI * Vec2.Cross(r1, P);
                b2._linearVelocity += b2._invMass * P;
                b2._angularVelocity += b2._invI * Vec2.Cross(r2, P);
            }
            else
            {
                _impulse = 0.0f;
            }
        }

        internal override bool SolvePositionConstraints(float baumgarte)
        {
            //B2_NOT_USED(baumgarte);

            if (_frequencyHz > 0.0f)
            {
                //There is no possition correction for soft distace constraint.
                return true;
            }

            Body b1 = _bodyA;
            Body b2 = _bodyB;

            Vec2 r1 = Math.Mul(b1.GetTransform().R, _localAnchorA - b1.GetLocalCenter());
            Vec2 r2 = Math.Mul(b2.GetTransform().R, _localAnchorB - b2.GetLocalCenter());

            Vec2 d = b2._sweep.C + r2 - b1._sweep.C - r1;

            float length = d.Normalize();
            float C = length - _length;
            C = Math.Clamp(C, -Settings.MaxLinearCorrection, Settings.MaxLinearCorrection);

            float impulse = -_mass * C;
            _u = d;
            Vec2 P = impulse * _u;

            b1._sweep.C -= b1._invMass * P;
            b1._sweep.A -= b1._invI * Vec2.Cross(r1, P);
            b2._sweep.C += b2._invMass * P;
            b2._sweep.A += b2._invI * Vec2.Cross(r2, P);

            b1.SynchronizeTransform();
            b2.SynchronizeTransform();

            return System.Math.Abs(C) < Settings.LinearSlop;
        }

        internal override void SolveVelocityConstraints(TimeStep step)
        {
            //B2_NOT_USED(step);

            Body b1 = _bodyA;
            Body b2 = _bodyB;

            Vec2 r1 = Math.Mul(b1.GetTransform().R, _localAnchorA - b1.GetLocalCenter());
            Vec2 r2 = Math.Mul(b2.GetTransform().R, _localAnchorB - b2.GetLocalCenter());

            // Cdot = dot(u, v + cross(w, r))
            Vec2 v1 = b1._linearVelocity + Vec2.Cross(b1._angularVelocity, r1);
            Vec2 v2 = b2._linearVelocity + Vec2.Cross(b2._angularVelocity, r2);
            float Cdot = Vec2.Dot(_u, v2 - v1);

            float impulse = -_mass * (Cdot + _bias + _gamma * _impulse);
            _impulse += impulse;

            Vec2 P = impulse * _u;
            b1._linearVelocity -= b1._invMass * P;
            b1._angularVelocity -= b1._invI * Vec2.Cross(r1, P);
            b2._linearVelocity += b2._invMass * P;
            b2._angularVelocity += b2._invI * Vec2.Cross(r2, P);
        }

        /// Set/get the natural length.
        public void SetLength(float length)
        {
            _length = length;
        }

        public float GetLength()
        {
            return _length;
        }

        /// Set/get frequency in Hz.
        public void SetFrequency(float hz)
        {
            _frequencyHz = hz;
        }

        public float GetFrequency()
        {
            return _frequencyHz;
        }

        /// Set/get damping ratio.
        public void SetDampingRatio(float ratio)
        {
            _dampingRatio = ratio;
        }

        public float GetDampingRatio()
        {
            return _dampingRatio;
        }
    }
}
