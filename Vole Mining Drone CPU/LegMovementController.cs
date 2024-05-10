using Sandbox.Definitions;
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
        public class LegMovementController : ISaveable
        {
            public enum MovementDirection
            {
                Down,
                Up
            }

            private LegDefinition leg;
            private float rotorRPM;
            private float pistonSpeed;
            private float returnMult;

            public LegDefinition GetLegDefinition() { return leg; }

            private Func<bool> shouldContinueFunc = () => true;

            #region SaveOnExitVariables
            private MovementDirection direction = MovementDirection.Down;
            public int MovementStep { get; private set; } = 0;
            private bool legSensorTiggered = false;
            private bool landigGearLocked = false;
            public bool ShouldMoveNextLeg { get; private set; } = false;
            public bool SequenceInProgress { get; private set; } = false;
            public bool IsDefaultState { get; private set; } = true;
            public bool IsPacked { get; private set; } = true;
            public bool IsDisabled { get; private set; } = true;
            #endregion

            #region ISaveable
            const char seperator = '\u0091'; // Private Use Unciode Control Character
            public string Serialize()
            {
                return string.Join(seperator.ToString(), direction, MovementStep, legSensorTiggered, landigGearLocked, ShouldMoveNextLeg, SequenceInProgress);
            }
            public bool Deserialize(string dataString)
            {
                // TODO: Decrement MovementStep on load to reload callbacks;
                return true;
            }
            public bool ShouldSerialize()
            {
                return !IsDefaultState;
            }
            #endregion

            public LegMovementController(LegDefinition definition, float rotorRPM = 2, float pistonSpeed = 0.5f, float returnMult = 1)
            {
                this.leg = definition; this.rotorRPM = rotorRPM; this.pistonSpeed = pistonSpeed; this.returnMult = returnMult; this.ShouldMoveNextLeg = false;
            }

            public bool TryMoveNext()
            {
                if (!shouldContinueFunc()) return false;
                if (direction == MovementDirection.Down)
                {
                    shouldContinueFunc = MovementDown();
                    MovementStep++;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public void Pack()
            {
                rotateStatorToAngle(leg.FootHinge, 0);
                leg.KneeHinge.TargetVelocityRPM = rotorRPM;
                leg.HipHingeVert.TargetVelocityRPM = rotorRPM;
                leg.HipHingeHoriz.RotorLock = false;
                leg.HipHingeHoriz.TargetVelocityRPM = rotorRPM;
                unlockMagnets();
                foreach (var magnet in leg.LegMagnets) { magnet.AutoLock = false; }
                leg.LowerLegPiston.Retract();
                leg.UpperLegPiston.Retract();
                leg.LegSenor.Enabled = false;
                direction = MovementDirection.Down;
                MovementStep = 0;
                legSensorTiggered = false;
                ShouldMoveNextLeg = false;
                SequenceInProgress = false;
                IsDefaultState = true;
            }

            public void Unpack()
            {
                leg.FootHinge.UpperLimitDeg = 90;
                leg.FootHinge.LowerLimitDeg = -90;
                leg.FootHinge.TargetVelocityRPM = 0;
                leg.KneeHinge.TargetVelocityRPM = -rotorRPM;
                leg.HipHingeVert.TargetVelocityRPM = -rotorRPM;
                leg.HipHingeHoriz.TargetVelocityRPM = -rotorRPM;
                foreach (var magnet in leg.LegMagnets) { magnet.AutoLock = true; }
                unlockMagnets();
                leg.LowerLegPiston.Extend();
                leg.UpperLegPiston.Extend();
                direction = MovementDirection.Down;
                MovementStep = 0;
            }

            public void EnableDisable(bool status)
            {
                leg.FootHinge.RotorLock = !status;
                leg.FootHinge.Enabled = status;
                leg.KneeHinge.RotorLock = !status;
                leg.KneeHinge.Enabled = status;
                leg.HipHingeVert.RotorLock = !status;
                leg.HipHingeVert.Enabled = status;
                leg.HipHingeHoriz.RotorLock = !status;
                leg.HipHingeHoriz.Enabled = status;
                leg.UpperLegPiston.Enabled = status;
                leg.LowerLegPiston.Enabled = status;
            }

            public void Enable() { shouldContinueFunc = () => true; }

            public void Disable() { shouldContinueFunc = () => false; }

            public void LegSensorTriggredCallback()
            {
                legSensorTiggered = true;
                if (direction == MovementDirection.Down)
                {
                    leg.HipHingeVert.RotorLock = true;
                    leg.UpperLegPiston.Enabled = false;
                }
            }

            public void LandingGearLockCallback()
            {
                landigGearLocked = true;
                if (direction == MovementDirection.Down)
                {
                    leg.LowerLegPiston.Enabled = false;
                }
            }

            // Downwards Movement Handling

            // Delegate should return true when we can continue
            private Func<bool> MovementDown()
            {
                switch (MovementStep)
                {
                    // Retract Lower Leg Piston
                    case 0:
                        unlockMagnets();
                        leg.LowerLegPiston.Enabled = true;
                        leg.LowerLegPiston.Retract();
                        SequenceInProgress = true;
                        // wait until Pistion is retracted > 50%
                        return () => leg.LowerLegPiston.NormalizedPosition < 0.5f;

                    // Retract all Hinges
                    case 1:
                        // Rotate HipHinge to 90°
                        leg.HipHingeVert.Enabled = true;
                        leg.HipHingeVert.RotorLock = false;
                        leg.HipHingeVert.TargetVelocityRPM = rotorRPM;
                        // Rotate KneeHinge to -90°
                        leg.KneeHinge.Enabled = true;
                        leg.KneeHinge.RotorLock = false;
                        leg.KneeHinge.TargetVelocityRPM = -rotorRPM;
                        // Rotate FootHinge to 0°
                        leg.FootHinge.Enabled = true;
                        leg.FootHinge.RotorLock = false;
                        rotateStatorToAngle(leg.FootHinge, 0);
                        // Wait for Hinges to reach end Position
                        return () => AreSimilar(leg.HipHingeVert.Angle, 90) && AreSimilar(leg.KneeHinge.Angle, -90) && AreSimilar(leg.FootHinge.Angle, 0);

                    case 2:
                        leg.UpperLegPiston.Enabled = true;
                        leg.UpperLegPiston.Retract();
                        return () => leg.UpperLegPiston.Status == PistonStatus.Retracted;
                    // Extend Hip Hinge down until 45°
                    case 3:
                        // Reset Foot Hinge limits
                        leg.FootHinge.TargetVelocityRPM = 0;
                        leg.FootHinge.UpperLimitDeg = 90;
                        leg.FootHinge.LowerLimitDeg = -90;
                        // Rotate Hip Hinge down
                        //leg.HipHingeVert.LowerLimitDeg = 45;
                        leg.HipHingeVert.TargetVelocityRPM = -rotorRPM;
                        leg.LegSenor.Enabled = true;
                        return () => legSensorTiggered || leg.HipHingeVert.Angle < 45;

                    case 4:
                        //leg.HipHingeVert.LowerLimitDeg = 0;
                        leg.UpperLegPiston.Extend();
                        return () => legSensorTiggered || AreSimilar(leg.HipHingeVert.Angle, 0);

                    case 5:
                        leg.LowerLegPiston.Enabled = true;
                        leg.LowerLegPiston.Extend();
                        return () => landigGearLocked || leg.LowerLegPiston.Status == PistonStatus.Extended;

                    case 6:
                        ShouldMoveNextLeg = true;
                        return () => false;

                    case 7:
                        return MoveCraftDown();

                    case 8:
                        leg.UpperLegPiston.Velocity = Math.Sign(leg.UpperLegPiston.Velocity) * pistonSpeed;
                        leg.LowerLegPiston.Velocity = Math.Sign(leg.LowerLegPiston.Velocity) * pistonSpeed;
                        leg.UpperLegPiston.Enabled = false;
                        leg.LowerLegPiston.Enabled = false;
                        leg.HipHingeVert.RotorLock = true;
                        leg.KneeHinge.RotorLock = true;
                        leg.FootHinge.RotorLock = true;
                        legSensorTiggered = false;
                        ShouldMoveNextLeg = false;
                        SequenceInProgress = false;
                        return () => true;
                    default:
                        break;
                }
                return () => false;
            }

            private Func<bool> MoveCraftDown()
            {
                leg.KneeHinge.Enabled = false;
                leg.KneeHinge.RotorLock = false;
                leg.FootHinge.Enabled = false;
                leg.FootHinge.Enabled = false;
                if (leg.HipHingeVert.Angle > 67.5) // Mostly Vertical, use only Upper Piston
                {
                    leg.UpperLegPiston.Enabled = true;
                    leg.UpperLegPiston.Extend();
                    ResetIsPistonStopped();
                    return () => IsPistonStopped(leg.UpperLegPiston);
                }
                else if (leg.HipHingeVert.Angle < 22.5) // Mostly Horizontal, use only Lower Piston
                {
                    leg.LowerLegPiston.Enabled = true;
                    leg.LowerLegPiston.Retract();
                    ResetIsPistonStopped();
                    return () => IsPistonStopped(leg.LowerLegPiston);
                }
                else // move each piston for smoother movement
                {
                    var p = leg.HipHingeVert.Angle / 90;
                    var upperSpeed = Lerp(0, pistonSpeed, p);
                    var lowerSpeed = Lerp(0, -pistonSpeed, 1 - p);
                    leg.UpperLegPiston.Velocity = upperSpeed;
                    leg.LowerLegPiston.Velocity = lowerSpeed;

                    // Stop when one Piston stopped moving (to be safe against clang)
                    ResetIsPistonStopped();
                    return () => IsPistonStopped(leg.UpperLegPiston) || IsPistonStopped(leg.LowerLegPiston);
                }
            }

            // Helper Functions

            private void rotateStatorToAngle(IMyMotorStator stator, float angle)
            {
                var sign = Math.Sign(DegreesToRadians(angle) - stator.Angle);
                stator.TargetVelocityRPM = sign * -rotorRPM;
                if (sign < 0)
                {
                    stator.UpperLimitRad = angle;
                }
                else
                {
                    stator.LowerLimitRad = angle;
                }
            }

            private void unlockMagnets()
            {
                foreach (var gear in leg.LegMagnets)
                {
                    gear.Unlock();
                    gear.ResetAutoLock();
                }
                landigGearLocked = false;
            }

            private float upperLegLastChange = 0;
            private bool upperLegChangeInit = false;
            private float lowerLegLastChange = 0;
            private bool lowerLegChangeInit = false;
            private void ResetIsPistonStopped()
            {
                upperLegLastChange = 0;
                upperLegChangeInit = false;
                lowerLegLastChange = 0;
                lowerLegChangeInit = false;
            }
            private bool IsPistonStopped(IMyPistonBase piston)
            {
                const float error = 0.005f;
                if (piston == leg.UpperLegPiston)
                {
                    if (leg.UpperLegPiston.Status != PistonStatus.Extending && leg.UpperLegPiston.Status != PistonStatus.Retracting) { upperLegLastChange = 0; return true; }
                    upperLegLastChange = Math.Abs(0 - leg.UpperLegPiston.NormalizedPosition);
                    if (AreSimilar(upperLegLastChange, 0, error) && upperLegChangeInit) { upperLegLastChange = 0; upperLegChangeInit = false; return true; }
                    upperLegChangeInit = true;
                }
                else
                {
                    if (leg.LowerLegPiston.Status != PistonStatus.Extending && leg.LowerLegPiston.Status != PistonStatus.Retracting) { lowerLegLastChange = 0; return true; }
                    lowerLegLastChange = Math.Abs(0 - leg.LowerLegPiston.NormalizedPosition);
                    if (AreSimilar(lowerLegLastChange, 0, error) && lowerLegChangeInit) { lowerLegLastChange = 0; lowerLegChangeInit = false; return true; }
                    lowerLegChangeInit = true;
                }
                return false;
            }

            private float Lerp(float from, float to, float by)
            {
                return from * (1 - by) + to * by;
            }

            private static float DegreesToRadians(float angle) => angle * ((float)Math.PI / 180.0f);
        }
    }
}
