using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public class Rotor
        {
            private readonly IMyMotorStator _rotor;
            public readonly float LowerLimitRad;
            public readonly float UpperLimitRad;
            public readonly float DefaultVelocity;
            private float _lastRotation;
            public float LastRotationRequest
            {
                get { return _lastRotation; }
            }

            public Rotor(IMyMotorStator rotor, float desiredVelocity, float lowerLimitRad = float.MinValue, float upperLimitRad = float.MaxValue)
            {
                this._rotor = rotor;
                this.UpperLimitRad = upperLimitRad;
                this.LowerLimitRad = lowerLimitRad;
                this.DefaultVelocity = desiredVelocity;
            }

            public bool RotateToRad(float angleRad, float desiredVelocity = 0)
            {
                desiredVelocity = desiredVelocity == 0 ? DefaultVelocity : desiredVelocity;
                // Get current Angle within 1 Rad
                float curAngle = _rotor.Angle % PI2;
                _rotor.RotorLock = false;

                // Return early if already at the desired angle
                if (AreSimilar(curAngle, angleRad)) return false;

                // Clamp desired angle within Rotation Limits
                angleRad = Clamp<float>(angleRad % PI2, LowerLimitRad, UpperLimitRad);

                // Get positive angle for direction calcualtions
                var absAngleRad = (angleRad < 0.0f) ? PI2 + angleRad : angleRad;
                var absCurAngle = (curAngle < 0.0f) ? PI2 + curAngle : curAngle;

                // Get sign of rotation
                int velocitySign = (absAngleRad - absCurAngle + PI2) % PI2 < Math.PI ? +1 : -1;

                // Check if one rotation direction would go though the limits;
                if (velocitySign == 1 && absAngleRad > UpperLimitRad) 
                {
                    velocitySign = -1;
                }
                else if (angleRad < LowerLimitRad)
                {
                    velocitySign = 1;
                }

                float finalSetVelocity = desiredVelocity * velocitySign;
                float finalSetAngle;

                // Calculat final Angle based on current Angle & rotation Direction
                if (curAngle < 0.0f)
                {
                    finalSetAngle = (angleRad > 0.0f && velocitySign < 0) ? -PI2 + angleRad : angleRad;
                }
                else
                {
                    finalSetAngle = (angleRad < 0.0f && velocitySign > 0) ? absAngleRad : angleRad;
                }

                //Set the appropriate limit   
                if (velocitySign > 0)
                {
                    _rotor.UpperLimitRad = finalSetAngle;
                }
                else
                {
                    _rotor.LowerLimitRad = finalSetAngle;
                }

                _rotor.TargetVelocityRPM = finalSetVelocity;
                _lastRotation = finalSetAngle;

                return true;
            }

            public bool RotateToDeg(float angleDeg, float desiredVelocity = 0)
            {
                return RotateToRad(DegreesToRadians(angleDeg), desiredVelocity);
            }

            public bool HasReachedTargetRotation()
            {
                return AreSimilar(_rotor.Angle, _lastRotation);
            }

            public void ResetToDefault()
            {
                // Set Velocity to 0
                _rotor.TargetVelocityRPM = 0;
                // Reset Limits to inital values;
                _rotor.UpperLimitRad = UpperLimitRad;
                _rotor.LowerLimitRad = LowerLimitRad;
            }

            private const float PI2 = (float)(Math.PI * 2);

            

            //public static float RadiansToDegrees(float angle) => angle * (180.0f / (float)Math.PI);

            //public static float DegreesToRadians(float angle) => angle * ((float)Math.PI / 180.0f);
        }
    }
}
