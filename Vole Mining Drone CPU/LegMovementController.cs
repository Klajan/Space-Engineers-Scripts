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
            public readonly LegDefinition LegDef;
            private readonly float rotorRPM;
            private readonly float pistonSpeed;
            private float returnMult;

            public LegDefinition GetLegDefinition() { return LegDef; }

            private Func<bool> shouldContinueFunc = () => true;

            #region SaveOnExitVariables
            public MovementDirection Direction = MovementDirection.Down;
            public MovementDirection RequestedDirection = MovementDirection.Down;
            public int SequenceStep { get; private set; } = 0;
            public bool SequenceInProgress { get; private set; } = false;
            public bool SequenceEnded { get; private set; } = false;
            private bool legSensorTiggered = false;
            private bool landigGearLocked = false;
            public bool ShouldMoveNextLeg { get; private set; } = false;
            public bool IsWaitingForContinue { get; private set; } = false;
            public bool IsPacked { get; private set; } = true;
            public bool IsDisabled { get; private set; } = true;
            public bool HasAscended { get; private set; } = false;
            private int ticksWaited = 0;
            #endregion


            #region ISaveable
            public ushort Salt { get { return 0x2002; } }
            private const int minDataLength = sizeof(int) * 4 + sizeof(bool) * 9;
            public byte[] Serialize()
            {
                // Using slower LINQ method for conacting arrays for now, should still be faster then string concat
                // const uint serialSize = sizeof(MovementDirection) + sizeof(int) + sizeof(bool);
                RequestedDirection = Direction;
                byte[] data = BitConverter.GetBytes((int)Direction)
                    .Concat(BitConverter.GetBytes((int)RequestedDirection))
                    .Concat(BitConverter.GetBytes(SequenceStep))
                    .Concat(BitConverter.GetBytes(SequenceInProgress))
                    .Concat(BitConverter.GetBytes(SequenceEnded))
                    .Concat(BitConverter.GetBytes(legSensorTiggered))
                    .Concat(BitConverter.GetBytes(landigGearLocked))
                    .Concat(BitConverter.GetBytes(ShouldMoveNextLeg))
                    .Concat(BitConverter.GetBytes(IsWaitingForContinue))
                    .Concat(BitConverter.GetBytes(IsPacked))
                    .Concat(BitConverter.GetBytes(IsDisabled))
                    .Concat(BitConverter.GetBytes(HasAscended))
                    .Concat(BitConverter.GetBytes(ticksWaited))
                    .ToArray();

                return data;
            }
            public bool Deserialize(byte[] data)
            {
                if (data != null && data.Length < minDataLength) return false;
                // TODO: Decrement MovementStep on load to reload callbacks;
                int index = 0;
                Direction = (MovementDirection)BitConverter.ToInt32(data, index);
                index += sizeof(int);
                RequestedDirection = (MovementDirection)BitConverter.ToInt32(data, index);
                index += sizeof(int);
                SequenceStep = Math.Max(0, BitConverter.ToInt32(data, index) - 1);
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
                ticksWaited = BitConverter.ToInt32(data, index);
                index += sizeof(int);
                Direction = RequestedDirection;
                return true;
            }
            public bool ShouldSerialize()
            {
                return SequenceInProgress || SequenceEnded || RequestedDirection != MovementDirection.Down;
            }
            #endregion

            public LegMovementController(LegDefinition definition, float rotorRPM = 3, float pistonSpeed = 0.75f, float returnMult = 1)
            {
                this.LegDef = definition; this.rotorRPM = rotorRPM; this.pistonSpeed = pistonSpeed; this.returnMult = returnMult; this.ShouldMoveNextLeg = false;
            }

            #region ISequence
            public bool TryNextStep()
            {
                if (!shouldContinueFunc()) return false;
                if (Direction != RequestedDirection && SequenceStep <= 2) // requested direction switch;
                {
                    Direction = RequestedDirection;
                    shouldContinueFunc = () => true;
                    RestartSequence(true);
                    return true;
                }
                shouldContinueFunc = RunSequenceStep();
                SequenceStep++;
                return true;
            }
            #endregion

            public void Pack()
            {
                LegDef.HipHingeVert.RotorLock = false;
                LegDef.KneeHinge.RotorLock = false;
                LegDef.FootHinge.RotorLock = false;
                LegDef.HipHingeHoriz.RotorLock = false;
                RotorTools.RotateToDeg(LegDef.FootHinge, 0);
                LegDef.KneeHinge.TargetVelocityRPM = rotorRPM;
                LegDef.KneeHinge.UpperLimitDeg = 0;
                LegDef.HipHingeVert.TargetVelocityRPM = rotorRPM;
                LegDef.HipHingeHoriz.TargetVelocityRPM = rotorRPM;
                UnlockMagnets();
                foreach (var magnet in LegDef.LegMagnets) { magnet.AutoLock = false; }
                LegDef.LowerLegPiston.Velocity = -rotorRPM * 2;
                LegDef.UpperLegPiston.Velocity = -rotorRPM * 1.5f;
                LegDef.LegSenor.Enabled = false;
                RequestedDirection = MovementDirection.Down;
                ResetFields();
            }

            public void Unpack()
            {
                LegDef.HipHingeVert.RotorLock = false;
                LegDef.KneeHinge.RotorLock = false;
                LegDef.FootHinge.RotorLock = false;
                LegDef.HipHingeHoriz.RotorLock = false;
                LegDef.FootHinge.UpperLimitDeg = 90;
                LegDef.FootHinge.LowerLimitDeg = -90;
                LegDef.FootHinge.TargetVelocityRPM = 0;
                LegDef.KneeHinge.LowerLimitDeg = -90;
                LegDef.KneeHinge.UpperLimitDeg = 0f;
                LegDef.KneeHinge.TargetVelocityRPM = -rotorRPM;
                LegDef.HipHingeVert.TargetVelocityRPM = -rotorRPM;
                LegDef.HipHingeHoriz.TargetVelocityRPM = -rotorRPM;
                foreach (var magnet in LegDef.LegMagnets) { magnet.AutoLock = true; }
                UnlockMagnets();
                LegDef.LowerLegPiston.Velocity = rotorRPM * 0.75f;
                LegDef.UpperLegPiston.Velocity = rotorRPM * 1.25f;
                RequestedDirection = MovementDirection.Down;
                ResetFields();
            }

            public void EnableDisable(bool status)
            {
                LegDef.FootHinge.RotorLock = !status;
                LegDef.FootHinge.Enabled = status;
                LegDef.KneeHinge.RotorLock = !status;
                LegDef.KneeHinge.Enabled = status;
                LegDef.HipHingeVert.RotorLock = !status;
                LegDef.HipHingeVert.Enabled = status;
                LegDef.HipHingeHoriz.RotorLock = !status;
                LegDef.HipHingeHoriz.Enabled = status;
                LegDef.UpperLegPiston.Enabled = status;
                LegDef.LowerLegPiston.Enabled = status;
            }

            public void ContinueSequence() { shouldContinueFunc = () => true; }

            public void Disable() { shouldContinueFunc = () => false; }

            private void ResetFields()
            {
                SequenceStep = 0;
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
                ResetFields();
                LegDef.UpperLegPiston.Velocity = 0;
                LegDef.LowerLegPiston.Velocity = 0;
                LegDef.HipHingeVert.Enabled = true;
                LegDef.KneeHinge.Enabled = true;
                LegDef.FootHinge.Enabled = true;
                LegDef.HipHingeVert.RotorLock = true;
                LegDef.KneeHinge.RotorLock = true;
                LegDef.FootHinge.RotorLock = false;
                LegDef.LegSenor.Enabled = false;
                shouldContinueFunc = () => true;
                return true;
            }

            public void SwitchDirection(MovementDirection direction)
            {
                this.RequestedDirection = direction;
            }

            public void LegSensorTriggredCallback()
            {
                legSensorTiggered = true;
                LegDef.KneeHinge.TargetVelocityRPM = 0;
                LegDef.HipHingeVert.TargetVelocityRPM = 0;
                LegDef.UpperLegPiston.Velocity = 0;
                SequenceStep = 6;
            }

            public void LandingGearLockCallback()
            {
                landigGearLocked = true;
                LegDef.LowerLegPiston.Velocity = 0;
            }

            private bool CheckLegSensor()
            {
                if (LegDef.LegSenor.IsActive && LegDef.LegSenor.IsWorking)
                {
                    LegSensorTriggredCallback();
                    return true;
                }
                return false;
            }

            private bool CheckMagnetLocked()
            {
                foreach (var gear in LegDef.LegMagnets)
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
                ticksWaited = 0;
                switch (SequenceStep)
                {
                    // Retract Lower Leg Piston
                    case 0:
                        SequenceInProgress = true;
                        ChangeMagnetAutoLock(false);
                        UnlockMagnets();
                        // Retract LowerLeg Piston
                        LegDef.LowerLegPiston.Velocity = -pistonSpeed * retMult; // = leg.LowerLegPiston.Retract();
                        // Unlock Rotor lock 1 step before to avoid a bug
                        LegDef.HipHingeHoriz.RotorLock = true;
                        LegDef.HipHingeVert.RotorLock = false;
                        LegDef.KneeHinge.RotorLock = false;
                        LegDef.FootHinge.RotorLock = false;
                        LegDef.HipHingeVert.Enabled = true;
                        LegDef.KneeHinge.Enabled = true;
                        LegDef.FootHinge.Enabled = true;
                        return () =>
                        {
                            UnlockMagnets(); // Sometimes magnets don't unlock...
                            return LegDef.LowerLegPiston.Status == PistonStatus.Retracted;
                        };

                    // Retract all Hinges
                    case 1:
                        const float hipHingeAngle = 90f;
                        const float kneeHineAngle = -15f;
                        const float footHingeAngle = 0f;
                        UnlockMagnets();
                        // Rotate HipHinge to 90°
                        LegDef.HipHingeVert.TargetVelocityRPM = rotorRPM * retMult;
                        // Rotate KneeHinge to -45°
                        LegDef.KneeHinge.TargetVelocityRPM = rotorRPM;
                        LegDef.KneeHinge.LowerLimitDeg = -15;
                        LegDef.KneeHinge.UpperLimitDeg = -15;
                        //RotorTools.RotateToDeg(LegDef.KneeHinge, kneeHineAngle, rotorRPM * retMult);
                        // Rotate FootHinge to 0°
                        RotorTools.RotateToDeg(LegDef.FootHinge, 0, rotorRPM * retMult);
                        // Slowly retract Upper Leg piston
                        LegDef.UpperLegPiston.Velocity = -0.075f; // = leg.UpperLegPiston.Retract();
                        // Wait for Hinges to reach end Position
                        return () =>
                        {
                            UnlockMagnets(); // Sometimes magnets don't unlock...
                            return RotorTools.IsRotorAtAngle(LegDef.HipHingeVert, hipHingeAngle)
                            && RotorTools.IsRotorAtAngle(LegDef.KneeHinge, kneeHineAngle)
                            && RotorTools.IsRotorAtAngle(LegDef.FootHinge, footHingeAngle);
                        };

                    // Extend or Retract Upper Leg piston baseed on movement direction
                    case 2:
                        if (RequestedDirection == MovementDirection.Down)
                        {
                            LegDef.UpperLegPiston.Velocity = -pistonSpeed * retMult; // = leg.UpperLegPiston.Retract();
                            return () => LegDef.UpperLegPiston.Status == PistonStatus.Retracted;
                        }
                        else
                        {
                            LegDef.UpperLegPiston.Velocity = pistonSpeed * retMult; // = leg.UpperLegPiston.Extend();
                            return () => LegDef.UpperLegPiston.Status == PistonStatus.Extended;
                        }
                    // Move Knee down until detecting a wall or reaching end
                    case 3:
                        LegDef.LegSenor.Enabled = true;
                        LegDef.KneeHinge.LowerLimitDeg = -90;
                        LegDef.KneeHinge.UpperLimitDeg = 0f;
                        LegDef.KneeHinge.TargetVelocityRPM = -rotorRPM;
                        // Reset Foot Hinge limits
                        LegDef.FootHinge.TargetVelocityRPM = 0;
                        LegDef.FootHinge.UpperLimitDeg = 90;
                        LegDef.FootHinge.LowerLimitDeg = -90;

                        return () => CheckLegSensor() || RotorTools.IsRotorAtAngle(LegDef.KneeHinge, -90f);

                    // Extend Hip Hinge down until 45°
                    case 4:
                        // Rotate Hip Hinge down
                        LegDef.HipHingeVert.TargetVelocityRPM = -rotorRPM;
                        return () => CheckLegSensor() || LegDef.HipHingeVert.Angle < DegreesToRadians(52);
                    case 5:
                        if (RequestedDirection == MovementDirection.Down)
                        {
                            LegDef.UpperLegPiston.Velocity = pistonSpeed; // = leg.UpperLegPiston.Extend();
                            return () => CheckLegSensor() || RotorTools.IsRotorAtAngle(LegDef.HipHingeVert, 0);
                        }
                        else
                        {
                            ChangeMagnetAutoLock(true);
                            return () =>
                            {
                                if (CheckMagnetLocked() || RotorTools.IsRotorAtAngle(LegDef.HipHingeVert, 0))
                                {
                                    LegSensorTriggredCallback(); return true;
                                }
                                return false;
                            };
                        }

                    case 6:
                        ChangeMagnetAutoLock(true);
                        LegDef.LegSenor.Enabled = false;
                        if (!landigGearLocked) LegDef.LowerLegPiston.Velocity = pistonSpeed; // = leg.LowerLegPiston.Extend();
                        return () => CheckMagnetLocked() || LegDef.LowerLegPiston.Status == PistonStatus.Extended;

                    case 7:
                        ShouldMoveNextLeg = true;
                        IsWaitingForContinue = true;
                        LegDef.HipHingeVert.RotorLock = true;
                        LegDef.KneeHinge.RotorLock = true;
                        LegDef.FootHinge.RotorLock = true;
                        return () => false;

                    case 8:
                        LegDef.HipHingeVert.RotorLock = false;
                        LegDef.KneeHinge.RotorLock = false;
                        LegDef.FootHinge.RotorLock = false;
                        return () => true;

                    case 9:
                        IsWaitingForContinue = false;
                        if (RequestedDirection == MovementDirection.Down)
                        {
                            return MoveCraftDown();
                        }
                        else
                        {
                            return MoveCraftUp();
                        }

                    case 10:
                        LegDef.LowerLegPiston.Velocity = 0;
                        LegDef.UpperLegPiston.Velocity = 0;
                        LegDef.HipHingeVert.Enabled = true;
                        LegDef.HipHingeVert.TargetVelocityRad = 0;
                        LegDef.KneeHinge.Enabled = true;
                        LegDef.KneeHinge.TargetVelocityRad = 0;
                        LegDef.FootHinge.Enabled = true;
                        LegDef.FootHinge.TargetVelocityRad = 0;
                        SequenceInProgress = false;
                        SequenceEnded = true;
                        if (RequestedDirection == MovementDirection.Up && LegDef.HipHingeVert.Angle < DegreesToRadians(15)) { HasAscended = true; }
                        return () => false;

                    default:
                        return () => true;
                }
            }

            // Downwards Movement Handling
            #region Downwards Movement

            private Func<bool> MoveCraftDown()
            {
                const int ticksToWait = 10;
                LegDef.HipHingeVert.Enabled = true;
                LegDef.KneeHinge.RotorLock = true;
                LegDef.FootHinge.Enabled = true;
                if (LegDef.HipHingeVert.Angle > DegreesToRadians(70)) // Mostly Vertical, use only Upper Piston
                {
                    if (LegDef.KneeHinge.Angle < DegreesToRadians(-75))
                    {
                        LegDef.FootHinge.Enabled = true;
                    }
                    LegDef.UpperLegPiston.Velocity = pistonSpeed; // = leg.UpperLegPiston.Extend();
                    return () => IsPistonStopped(LegDef.UpperLegPiston);
                }
                else if (LegDef.HipHingeVert.Angle < DegreesToRadians(20)) // Mostly Horizontal, use only Lower Piston
                {
                    LegDef.LowerLegPiston.Velocity = -pistonSpeed; // = leg.LowerLegPiston.Retract();
                    return () => IsPistonStopped(LegDef.LowerLegPiston);
                }
                else if (LegDef.HipHingeVert.Angle > DegreesToRadians(45))
                {
                    LegDef.HipHingeVert.Enabled = true;
                    LegDef.UpperLegPiston.Velocity = pistonSpeed;
                    //LegDef.UpperLegPiston.Extend();
                    LegDef.LowerLegPiston.Velocity = -pistonSpeed * 0.75f;
                    //LegDef.LowerLegPiston.Retract();
                    return () => WaitForTicks(ticksToWait) || IsPistonStopped(LegDef.UpperLegPiston);
                }
                //else // move each piston for smoother movement
                {
                    LegDef.HipHingeVert.Enabled = true;
                    LegDef.UpperLegPiston.Velocity = pistonSpeed * 0.75f;
                    //LegDef.UpperLegPiston.Extend();
                    LegDef.LowerLegPiston.Velocity = -pistonSpeed;
                    //LegDef.LowerLegPiston.Retract();
                    return () => WaitForTicks(ticksToWait) || IsPistonStopped(LegDef.LowerLegPiston);
                }
            }
            #endregion

            #region Upwards Movement
            private Func<bool> MoveCraftUp()
            {
                const int ticksToWait = 10;
                LegDef.FootHinge.Enabled = true;
                LegDef.KneeHinge.RotorLock = true;
                LegDef.FootHinge.UpperLimitDeg = 35;
                LegDef.FootHinge.LowerLimitDeg = -35;
                if (LegDef.HipHingeVert.Angle > DegreesToRadians(70)) // Mostly Vertical, use only Upper Piston
                {
                    LegDef.HipHingeVert.Enabled = false;
                    LegDef.UpperLegPiston.Velocity = -pistonSpeed; // = leg.UpperLegPiston.Retract();
                    return () => IsPistonStopped(LegDef.UpperLegPiston);
                }
                else if (LegDef.HipHingeVert.Angle < DegreesToRadians(20)) // Mostly Horizontal, check if Ascend is done
                {
                    LegDef.HipHingeVert.Enabled = true;
                    LegDef.LowerLegPiston.Velocity = pistonSpeed; // = leg.LowerLegPiston.Extend();
                    return () => (LegDef.HipHingeVert.Angle < DegreesToRadians(15) && IsPistonStopped(LegDef.LowerLegPiston));
                }
                else if (LegDef.HipHingeVert.Angle > DegreesToRadians(45))
                {
                    LegDef.HipHingeVert.Enabled = true;
                    LegDef.UpperLegPiston.Velocity = -pistonSpeed;
                    //LegDef.UpperLegPiston.Extend();
                    LegDef.LowerLegPiston.Velocity = pistonSpeed * 0.75f;
                    //LegDef.LowerLegPiston.Retract();
                    return () => WaitForTicks(ticksToWait) || IsPistonStopped(LegDef.UpperLegPiston);
                }
                //else // move each piston for smoother movement
                {
                    //LegDef.HipHingeVert.Enabled = true;
                    LegDef.UpperLegPiston.Velocity = -pistonSpeed * 0.75f;
                    //LegDef.UpperLegPiston.Extend();
                    LegDef.LowerLegPiston.Velocity = pistonSpeed;
                    //LegDef.LowerLegPiston.Retract();
                    return () => WaitForTicks(ticksToWait) || IsPistonStopped(LegDef.LowerLegPiston);
                }
                /*else if (LegDef.HipHingeVert.Angle > DegreesToRadians(45))
                {
                    LegDef.HipHingeVert.Enabled = true;
                    LegDef.UpperLegPiston.Velocity = -pistonSpeed;
                    //LegDef.UpperLegPiston.Retract();
                    LegDef.LowerLegPiston.Velocity = pistonSpeed / 2;
                    //LegDef.LowerLegPiston.Extend();
                    return () => IsPistonStopped(LegDef.UpperLegPiston);
                }
                else // move each piston for smoother movement
                {
                    LegDef.HipHingeVert.Enabled = true;
                    LegDef.UpperLegPiston.Velocity = -(pistonSpeed / 2);
                    //LegDef.UpperLegPiston.Retract();
                    LegDef.LowerLegPiston.Velocity = pistonSpeed;
                    //LegDef.LowerLegPiston.Extend();
                    return () => IsPistonStopped(LegDef.LowerLegPiston);
                }*/
            }
            #endregion

            // Helper Functions

            private void UnlockMagnets()
            {
                foreach (var gear in LegDef.LegMagnets)
                {
                    gear.Unlock();
                }
                landigGearLocked = false;
            }
            private void ChangeMagnetAutoLock(bool status)
            {
                foreach (var gear in LegDef.LegMagnets)
                {
                    gear.AutoLock = status;
                }
            }
            private void ToggleMagnets(bool status)
            {
                foreach (var gear in LegDef.LegMagnets)
                {
                    gear.Enabled = status;
                }
            }

            private float pistonLastPos = -1;
            private bool IsPistonStopped(IMyPistonBase piston)
            {
                const float error = 0.0005f;
                float delta;
                if (!(piston.Status == PistonStatus.Extending || piston.Status == PistonStatus.Retracting)) { pistonLastPos = -1; return true; }
                delta = Math.Abs(pistonLastPos - piston.NormalizedPosition);
                if (AreSimilar(delta, 0, error)) { pistonLastPos = -1; return true; }
                pistonLastPos = piston.NormalizedPosition;
                return false;
            }

            private bool WaitForTicks(int ticksToWait)
            {
                ticksWaited++;
                if (ticksWaited > ticksToWait)
                {
                    ticksWaited = 0;
                    return true;
                }
                return false;
            }
        }
    }
}
