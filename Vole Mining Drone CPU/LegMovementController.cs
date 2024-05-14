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
        public class LegMovementController : ISequence, ISaveable
        {
            private readonly LegDefinition leg;
            private readonly float rotorRPM;
            private readonly float pistonSpeed;
            private float returnMult;

            public LegDefinition GetLegDefinition() { return leg; }

            private Func<bool> shouldContinueFunc = () => true;

            #region SaveOnExitVariables
            private MovementDirection direction = MovementDirection.Down;
            public int MovementStep { get; private set; } = 0;
            public bool SequenceInProgress { get; private set; } = false;
            public bool SequenceEnded { get; private set; } = false;
            private bool legSensorTiggered = false;
            private bool landigGearLocked = false;
            public bool ShouldMoveNextLeg { get; private set; } = false;
            public bool IsWaitingForContinue { get; private set; } = false;
            public bool IsPacked { get; private set; } = true;
            public bool IsDisabled { get; private set; } = true;
            public bool HasAscended { get; private set; } = false;
            #endregion

            #region ISaveable
            private const int minDataLength = sizeof(int) * 2 + sizeof(bool) * 9;
            public byte[] Serialize()
            {
                // Using slower LINQ method for conacting arrays for now, should still be faster then string concat
                // const uint serialSize = sizeof(MovementDirection) + sizeof(int) + sizeof(bool);
                byte[] data = BitConverter.GetBytes((int)direction)
                    .Concat(BitConverter.GetBytes(MovementStep))
                    .Concat(BitConverter.GetBytes(SequenceInProgress))
                    .Concat(BitConverter.GetBytes(SequenceEnded))
                    .Concat(BitConverter.GetBytes(legSensorTiggered))
                    .Concat(BitConverter.GetBytes(landigGearLocked))
                    .Concat(BitConverter.GetBytes(ShouldMoveNextLeg))
                    .Concat(BitConverter.GetBytes(IsWaitingForContinue))
                    .Concat(BitConverter.GetBytes(IsPacked))
                    .Concat(BitConverter.GetBytes(IsDisabled))
                    .Concat(BitConverter.GetBytes(HasAscended))
                    .ToArray();

                return data;
            }
            public bool Deserialize(byte[] data)
            {
                if (data != null && data.Length < minDataLength) return false;
                // TODO: Decrement MovementStep on load to reload callbacks;
                int index = 0;
                direction = (MovementDirection)BitConverter.ToInt32(data, index);
                index += sizeof(int);
                MovementStep = Math.Max(0, BitConverter.ToInt32(data, index) - 1);
                index += sizeof(int);
                SequenceInProgress = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                SequenceEnded = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                legSensorTiggered = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                landigGearLocked = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                ShouldMoveNextLeg = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                IsWaitingForContinue = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                IsPacked = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                IsDisabled = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                HasAscended = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                return true;
            }
            public bool ShouldSerialize()
            {
                return SequenceInProgress || SequenceEnded || direction != MovementDirection.Down;
            }
            #endregion

            public LegMovementController(LegDefinition definition, float rotorRPM = 3, float pistonSpeed = 0.75f, float returnMult = 1)
            {
                this.leg = definition; this.rotorRPM = rotorRPM; this.pistonSpeed = pistonSpeed; this.returnMult = returnMult; this.ShouldMoveNextLeg = false;
            }

            #region ISequence
            public UpdateFrequency MyUpdateFrequency { get; private set; } = UpdateFrequency.Update10;

            public bool TryNextStep()
            {
                if (!shouldContinueFunc()) return false;
                shouldContinueFunc = RunSequenceStep();
                MovementStep++;
                return true;
            }
            #endregion

            public void Pack()
            {
                RotorTools.RotateToDeg(leg.FootHinge, 0);
                leg.KneeHinge.TargetVelocityRPM = rotorRPM;
                leg.HipHingeVert.TargetVelocityRPM = rotorRPM;
                leg.HipHingeHoriz.RotorLock = false;
                leg.HipHingeHoriz.TargetVelocityRPM = rotorRPM;
                UnlockMagnets();
                foreach (var magnet in leg.LegMagnets) { magnet.AutoLock = false; }
                leg.LowerLegPiston.Velocity = -rotorRPM * 2;
                leg.UpperLegPiston.Velocity = -rotorRPM * 1.5f;
                leg.LegSenor.Enabled = false;
                direction = MovementDirection.Down;
                resetFields();
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
                UnlockMagnets();
                leg.LowerLegPiston.Velocity = rotorRPM * 0.75f;
                leg.UpperLegPiston.Velocity = rotorRPM * 1.25f;
                direction = MovementDirection.Down;
                resetFields();
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

            public void ContinueSequence() { shouldContinueFunc = () => true; }

            public void Disable() { shouldContinueFunc = () => false; }

            private void resetFields()
            {
                MovementStep = 0;
                SequenceInProgress = false;
                SequenceEnded = false;
                IsWaitingForContinue = false;
                ShouldMoveNextLeg = false;
                legSensorTiggered = false;
                landigGearLocked = false;
                IsPacked = false;
                IsDisabled = false;
                HasAscended = false;
            }

            public bool RestartSequence(bool force = false)
            {
                if (!SequenceEnded && !force) return false;
                resetFields();
                leg.UpperLegPiston.Velocity = 0;
                leg.LowerLegPiston.Velocity = 0;
                leg.HipHingeVert.Enabled = true;
                leg.KneeHinge.Enabled = true;
                leg.FootHinge.Enabled = true;
                //leg.HipHingeVert.RotorLock = true;
                //leg.KneeHinge.RotorLock = true;
                //leg.FootHinge.RotorLock = true;
                leg.LegSenor.Enabled = false;
                shouldContinueFunc = () => true;
                return true;
            }

            public bool SwitchDirection(MovementDirection direction)
            {
                if (MovementStep > 2) return false;
                this.direction = direction;
                //EchoDebug(MovementStep.ToString());
                RestartSequence(true);
                return true;
            }

            public void LegSensorTriggredCallback()
            {
                legSensorTiggered = true;
                leg.KneeHinge.TargetVelocityRPM = 0;
                //leg.KneeHinge.RotorLock = true;
                leg.HipHingeVert.TargetVelocityRPM = 0;
                //leg.HipHingeVert.RotorLock = true;
                leg.UpperLegPiston.Velocity = 0;
                MovementStep = 6;
            }

            public void LandingGearLockCallback()
            {
                landigGearLocked = true;
                leg.LowerLegPiston.Velocity = 0;
            }

            private bool CheckLegSensor()
            {
                if (leg.LegSenor.IsActive && leg.LegSenor.IsWorking)
                {
                    LegSensorTriggredCallback();
                    return true;
                }
                return false;
            }

            private bool CheckMagnetLocked()
            {
                foreach (var gear in leg.LegMagnets)
                {
                    if (gear.IsLocked && gear.IsWorking)
                    {
                        LandingGearLockCallback();
                        return true;
                    }
                }
                return false;
            }

            // Shared movement steps for up & down
            private Func<bool> RunSequenceStep()
            {
                const float retMult = 2f;
                switch (MovementStep)
                {
                    // Retract Lower Leg Piston
                    case 0:
                        SequenceInProgress = true;
                        ChangeMagnetAutoLock(false);
                        UnlockMagnets();
                        // Retract LowerLeg Piston
                        leg.LowerLegPiston.Velocity = -pistonSpeed * retMult; // = leg.LowerLegPiston.Retract();
                        // Unlock Rotor lock 1 step before to avoid a bug
                        leg.HipHingeVert.RotorLock = false;
                        leg.KneeHinge.RotorLock = false;
                        leg.FootHinge.RotorLock = false;
                        return () =>
                        {
                            //UnlockMagnets();
                            return leg.LowerLegPiston.Status == PistonStatus.Retracted;
                        };

                    // Retract all Hinges
                    case 1:
                        const float hipHingeAngle = 90f;
                        const float kneeHineAngle = -35f;
                        const float footHingeAngle = 0f;
                        UnlockMagnets();
                        // Rotate HipHinge to 90°
                        leg.HipHingeVert.Enabled = true;
                        leg.HipHingeVert.TargetVelocityRPM = rotorRPM * retMult;
                        // Rotate KneeHinge to -45°
                        leg.KneeHinge.Enabled = true;
                        //leg.KneeHinge.TargetVelocityRPM = -rotorRPM;
                        RotorTools.RotateToDeg(leg.KneeHinge, kneeHineAngle, rotorRPM * retMult);
                        // Rotate FootHinge to 0°
                        leg.FootHinge.Enabled = true;
                        RotorTools.RotateToDeg(leg.FootHinge, 0, rotorRPM * retMult);
                        // Slowly retract Upper Leg piston
                        leg.UpperLegPiston.Velocity = -0.075f; // = leg.UpperLegPiston.Retract();
                        // Wait for Hinges to reach end Position
                        return () =>
                        {
                            //UnlockMagnets();
                            return RotorTools.IsRotorAtAngle(leg.HipHingeVert, hipHingeAngle)
                            && RotorTools.IsRotorAtAngle(leg.KneeHinge, kneeHineAngle)
                            && RotorTools.IsRotorAtAngle(leg.FootHinge, footHingeAngle);
                        };

                    // Extend or Retract Upper Leg piston baseed on movement direction
                    case 2:
                        if (direction == MovementDirection.Down)
                        {
                            leg.UpperLegPiston.Velocity = -pistonSpeed * retMult; // = leg.UpperLegPiston.Retract();
                            return () => leg.UpperLegPiston.Status == PistonStatus.Retracted;
                        }
                        else
                        {
                            leg.UpperLegPiston.Velocity = pistonSpeed * retMult; // = leg.UpperLegPiston.Extend();
                            return () => leg.UpperLegPiston.Status == PistonStatus.Extended;
                        }
                    // Move Knee down until detecting a wall or reaching end
                    case 3:
                        leg.LegSenor.Enabled = true;
                        leg.KneeHinge.LowerLimitDeg = -90;
                        leg.KneeHinge.UpperLimitDeg = 0f;
                        leg.KneeHinge.TargetVelocityRPM = -rotorRPM;
                        // Reset Foot Hinge limits
                        leg.FootHinge.TargetVelocityRPM = 0;
                        leg.FootHinge.UpperLimitDeg = 90;
                        leg.FootHinge.LowerLimitDeg = -90;

                        return () => CheckLegSensor() || RotorTools.IsRotorAtAngle(leg.KneeHinge, -90f);

                    // Extend Hip Hinge down until 45°
                    case 4:
                        // Rotate Hip Hinge down
                        leg.HipHingeVert.TargetVelocityRPM = -rotorRPM;
                        return () => CheckLegSensor() || leg.HipHingeVert.Angle < DegreesToRadians(52);
                    case 5:
                        if(direction == MovementDirection.Down)
                        {
                            leg.UpperLegPiston.Velocity = pistonSpeed; // = leg.UpperLegPiston.Extend();
                            return () => CheckLegSensor() || RotorTools.IsRotorAtAngle(leg.HipHingeVert, 0);
                        }
                        else
                        {
                            return () => CheckLegSensor() || RotorTools.IsRotorAtAngle(leg.HipHingeVert, 0);
                        }

                    case 6:
                        ChangeMagnetAutoLock(true);
                        leg.LegSenor.Enabled = false;
                        leg.LowerLegPiston.Velocity = pistonSpeed; // = leg.LowerLegPiston.Extend();
                        return () => CheckMagnetLocked() || leg.LowerLegPiston.Status == PistonStatus.Extended;

                    case 7:
                        ShouldMoveNextLeg = true;
                        IsWaitingForContinue = true;
                        return () => false;

                    case 8:
                        IsWaitingForContinue = false;
                        ResetIsPistonStopped();
                        if (direction == MovementDirection.Down)
                        {
                            return MoveCraftDown();
                        }
                        else
                        {
                            return MoveCraftUp();
                        }

                    case 9:
                        leg.LowerLegPiston.Velocity = 0;
                        leg.UpperLegPiston.Velocity = 0;
                        leg.HipHingeVert.Enabled = true;
                        leg.HipHingeVert.TargetVelocityRad = 0;
                        leg.KneeHinge.Enabled = true;
                        leg.KneeHinge.TargetVelocityRad = 0;
                        leg.FootHinge.Enabled = true;
                        leg.FootHinge.TargetVelocityRad = 0;
                        SequenceInProgress = false;
                        SequenceEnded = true;
                        if(direction == MovementDirection.Up && leg.HipHingeVert.Angle < DegreesToRadians(15)) { HasAscended = true; }
                        return () => false;

                    default:
                        return () => false;
                }
            }

            // Downwards Movement Handling
            #region Downwards Movement
            
            private Func<bool> MoveCraftDown()
            {
                leg.HipHingeVert.Enabled = false;
                leg.HipHingeVert.RotorLock = false;
                leg.KneeHinge.Enabled = false;
                leg.KneeHinge.RotorLock = false;
                leg.FootHinge.Enabled = false;
                leg.FootHinge.RotorLock = false;
                if (leg.HipHingeVert.Angle > DegreesToRadians(65)) // Mostly Vertical, use only Upper Piston
                {
                    leg.UpperLegPiston.Velocity = pistonSpeed; // = leg.UpperLegPiston.Extend();
                    return () => IsPistonStopped(leg.UpperLegPiston);
                }
                else if (leg.HipHingeVert.Angle < DegreesToRadians(25)) // Mostly Horizontal, use only Lower Piston
                {
                    leg.LowerLegPiston.Velocity = -pistonSpeed; // = leg.LowerLegPiston.Retract();
                    return () => IsPistonStopped(leg.LowerLegPiston);
                }
                else // move each piston for smoother movement
                {
                    var p = leg.HipHingeVert.Angle / DegreesToRadians(90); // calculate % Angle
                    var upperSpeed = Lerp(0, pistonSpeed, p);
                    var lowerSpeed = Lerp(0, -pistonSpeed, 1 - p);
                    leg.UpperLegPiston.Velocity = upperSpeed;
                    leg.LowerLegPiston.Velocity = lowerSpeed;
                    IMyPistonBase pStop = leg.LowerLegPiston;
                    if (p > 0.5)
                    {
                        pStop = leg.UpperLegPiston;
                    }

                    // Stop when one Piston stopped moving (to be safe against clang)
                    //return () => IsPistonStopped(leg.UpperLegPiston) && IsPistonStopped(leg.LowerLegPiston);
                    return () =>
                    {
                        return leg.UpperLegPiston.NormalizedPosition > p
                        && leg.LowerLegPiston.NormalizedPosition < 1 - p
                        && IsPistonStopped(pStop);
                    };
                }
            }
            #endregion

            #region Upwards Movement
            private Func<bool> MoveCraftUp()
            {
                
                leg.HipHingeVert.RotorLock = false;
                leg.KneeHinge.RotorLock = false;
                leg.FootHinge.Enabled = false;
                leg.FootHinge.RotorLock = false;
                leg.FootHinge.UpperLimitDeg = 35;
                leg.FootHinge.LowerLimitDeg = -35;
                if (leg.HipHingeVert.Angle > DegreesToRadians(65)) // Mostly Vertical, use only Upper Piston
                {
                    leg.HipHingeVert.Enabled = false;
                    leg.KneeHinge.Enabled = false;
                    leg.UpperLegPiston.Enabled = true;
                    leg.UpperLegPiston.Velocity = -pistonSpeed; // = leg.UpperLegPiston.Retract();
                    return () => IsPistonStopped(leg.UpperLegPiston);
                }
                else if (leg.HipHingeVert.Angle < DegreesToRadians(30)) // Mostly Horizontal, Ascend should be done
                {
                    leg.HipHingeVert.Enabled = true;
                    leg.KneeHinge.Enabled = true;
                    leg.LowerLegPiston.Enabled = true;
                    leg.LowerLegPiston.Velocity = pistonSpeed; // = leg.LowerLegPiston.Extend();
                    leg.HipHingeVert.TargetVelocityRPM = -rotorRPM;
                    return () => (leg.HipHingeVert.Angle < DegreesToRadians(15) && IsPistonStopped(leg.LowerLegPiston));
                }
                else // move each piston for smoother movement
                {
                    leg.UpperLegPiston.Enabled = true;
                    leg.LowerLegPiston.Enabled = true;
                    leg.HipHingeVert.Enabled = true;
                    leg.KneeHinge.Enabled = true;
                    var p = leg.HipHingeVert.Angle / DegreesToRadians(90);
                    var upperSpeed = Lerp(0, -pistonSpeed, p);
                    var lowerSpeed = Lerp(0, pistonSpeed, 1 - p);
                    leg.UpperLegPiston.Velocity = upperSpeed;
                    leg.LowerLegPiston.Velocity = lowerSpeed;
                    leg.HipHingeVert.TargetVelocityRPM = -rotorRPM;
                    IMyPistonBase pStop = leg.LowerLegPiston;
                    if (p > 0.5)
                    {
                        pStop = leg.UpperLegPiston;
                    }
                    // Stop when one Piston stopped moving (to be safe against clang)
                    return () =>
                    {
                        return (leg.UpperLegPiston.NormalizedPosition < p
                        && leg.LowerLegPiston.NormalizedPosition > 1 - p)
                        && IsPistonStopped(pStop);
                    };
                }
            }
            #endregion

            // Helper Functions

            private void UnlockMagnets()
            {
                foreach (var gear in leg.LegMagnets)
                {
                    gear.Unlock();
                }
                landigGearLocked = false;
            }
            private void ChangeMagnetAutoLock(bool status)
            {
                foreach (var gear in leg.LegMagnets)
                {
                    gear.AutoLock = status;
                }
            }
            private void ToggleMagnets(bool status)
            {
                foreach (var gear in leg.LegMagnets)
                {
                    gear.Enabled = status;
                }
            }

            private float upperLegLastPos = -1;
            private float lowerLegLastPos = -1;
            private void ResetIsPistonStopped()
            {
                upperLegLastPos = -1;
                lowerLegLastPos = -1;
            }
            private bool IsPistonStopped(IMyPistonBase piston)
            {
                const float error = 0.0005f;
                float delta;
                if (piston == leg.UpperLegPiston)
                {
                    if (!(leg.UpperLegPiston.Status == PistonStatus.Extending || leg.UpperLegPiston.Status == PistonStatus.Retracting)) { upperLegLastPos = -1; return true; }
                    delta = Math.Abs(upperLegLastPos - leg.UpperLegPiston.NormalizedPosition);
                    //EchoDebug("IsPistonStopped delta:" + delta.ToString());
                    if (AreSimilar(delta, 0, error)) { upperLegLastPos = -1; return true; }
                    upperLegLastPos = leg.UpperLegPiston.NormalizedPosition;
                }
                else
                {
                    if (!(leg.LowerLegPiston.Status == PistonStatus.Extending || leg.LowerLegPiston.Status == PistonStatus.Retracting)) { lowerLegLastPos = -1; return true; }
                    delta = Math.Abs(lowerLegLastPos - leg.LowerLegPiston.NormalizedPosition);
                    //EchoDebug("IsPistonStopped delta:" + delta.ToString());
                    if (AreSimilar(delta, 0, error)) { lowerLegLastPos = -1; return true; }
                    lowerLegLastPos = leg.LowerLegPiston.NormalizedPosition;
                }
                return false;
            }

        }
    }
}
