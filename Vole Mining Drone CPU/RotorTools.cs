using Sandbox.Game.Entities.Cube;
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
        public static class RotorTools
        {
            private const float PI2 = (float)(Math.PI * 2);

            public static bool RotateToRad(IMyMotorStator rotor, float angleRad, float desiredVelocity = 1)
            {
                // Get current Angle within 1 Rad
                float curAngle = rotor.Angle % PI2;
                rotor.RotorLock = false;

                // Return early if already at the desired angle
                if (AreSimilar(curAngle, angleRad)) return false;

                // Clamp desired angle within Rotation Limits
                angleRad = Clamp<float>(angleRad % PI2, rotor.LowerLimitRad, rotor.UpperLimitRad);

                // Get positive angle for direction calcualtions
                var absAngleRad = (angleRad < 0.0f) ? PI2 + angleRad : angleRad;
                var absCurAngle = (curAngle < 0.0f) ? PI2 + curAngle : curAngle;

                // Get sign of rotation
                int velocitySign = (absAngleRad - absCurAngle + PI2) % PI2 < Math.PI ? +1 : -1;

                // Check if one rotation direction would go though the limits;
                if (velocitySign == 1 && absAngleRad > rotor.UpperLimitRad) 
                {
                    velocitySign = -1;
                }
                else if (angleRad < rotor.LowerLimitRad)
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
                    rotor.UpperLimitRad = finalSetAngle;
                }
                else
                {
                    rotor.LowerLimitRad = finalSetAngle;
                }

                rotor.TargetVelocityRPM = finalSetVelocity;

                return true;
            }

            public static bool RotateToDeg(IMyMotorStator rotor, float angleDeg, float desiredVelocity = 0)
            {
                return RotateToRad(rotor, DegreesToRadians(angleDeg), desiredVelocity);
            }

            public static bool IsRotorAtAngle(IMyMotorStator rotor, float angle, float error = 0.0001f)
            {
                return (Math.Abs(rotor.Angle - DegreesToRadians(angle)) < error);
            }

            public static bool IsRotorLessThanAngle(IMyMotorStator rotor, float angle)
            {
                return rotor.Angle < DegreesToRadians(angle);
            }

            public static bool IsRotorGreaterThanAngle(IMyMotorStator rotor, float angle)
            {
                return rotor.Angle > DegreesToRadians(angle);
            }


            //public static float RadiansToDegrees(float angle) => angle * (180.0f / (float)Math.PI);

            //public static float DegreesToRadians(float angle) => angle * ((float)Math.PI / 180.0f);
        }
    }
}
