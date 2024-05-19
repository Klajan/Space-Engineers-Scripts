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
        public class DrillController : ISequence, ISaveable
        {
            public DrillDefinition DrillDef { get; }
            private Func<bool> shouldContinueFunc = () => true;
            private float drillingSpeed;
            private float drillingRpm;
            private float drillingSpeedSlow;
            private const float returnSpeed = 1.0f;
            private uint periodicCheck = 0;
            private const float slowSpeedThreshold = 0.6f;

            #region SaveOnExitVariables
            private int SequenceStep = 0;
            public bool SequenceInProgress { get; private set; } = false;
            public bool EnableEjectors = true;
            private int ticksWaited = 0;
            public bool IsPacked { get; private set; } = true;
            #endregion

            public DrillController(DrillDefinition drill, float drillingSpeed = 0.05f, float drillingRpm = 3)
            {
                this.DrillDef = drill;
                this.drillingSpeed = drillingSpeed;
                this.drillingSpeedSlow = drillingSpeed / 3;
                this.drillingRpm = drillingRpm;
            }

            #region ISavable
            public ushort Salt { get { return 0x3002; } }
            private const int minDataLength = sizeof(int) * 2 + sizeof(bool) * 3;
            public bool ShouldSerialize()
            {
                return !IsPacked;
            }
            public byte[] Serialize()
            {
                byte[] data = BitConverter.GetBytes(SequenceStep)
                    .Concat(BitConverter.GetBytes(SequenceInProgress))
                    .Concat(BitConverter.GetBytes(EnableEjectors))
                    .Concat(BitConverter.GetBytes(ticksWaited))
                    .Concat(BitConverter.GetBytes(IsPacked))
                    .ToArray();
                return data;
            }
            public bool Deserialize(byte[] data)
            {
                if (data != null && data.Length < minDataLength) return false;
                int index = 0;
                SequenceStep = Math.Max(0, BitConverter.ToInt32(data, index) - 1);
                index += sizeof(int);
                SequenceInProgress = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                EnableEjectors = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                ticksWaited = BitConverter.ToInt32(data, index);
                index += sizeof(int);
                IsPacked = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                return true;
            }
            #endregion

            #region ISequence
            public bool TryNextStep()
            {
                RunPeriodicCheck();
                if (!shouldContinueFunc()) return false;
                shouldContinueFunc = RunSequence();
                SequenceStep++;
                return true;
            }

            public void Pack()
            {
                foreach (var ejector in DrillDef.EjectorConnectors) { ejector.CollectAll = false; }
                foreach (var drill in DrillDef.ShipDrills) { drill.Enabled = false; }
                DrillDef.Motor.Enabled = true;
                DrillDef.Motor.UpperLimitRad = 0;
                DrillDef.Motor.LowerLimitRad = 0;
                DrillDef.Motor.TargetVelocityRPM = drillingRpm;
                DrillDef.Piston.Velocity = returnSpeed;
                DrillDef.Piston.Enabled = true;
                DrillDef.Piston.Retract();
                DrillDef.Sensor.Enabled = false;
                IsPacked = true;
                SequenceStep = 0;
            }

            public void Unpack()
            {
                DrillDef.Motor.Enabled = true;
                DrillDef.Motor.TargetVelocityRPM = 0;
                DrillDef.Motor.UpperLimitRad = float.MaxValue;
                DrillDef.Motor.LowerLimitDeg = float.MinValue;
                DrillDef.Sensor.Enabled = true;
                IsPacked = false;
                SequenceStep = 0;
            }

            #endregion

            private Func<bool> RunSequence()
            {
                ticksWaited = 0;
                switch (SequenceStep)
                {
                    case 0:
                        SequenceInProgress = true;
                        foreach (var drill in DrillDef.ShipDrills) { drill.Enabled = true; }
                        if (EnableEjectors) { foreach (var ejector in DrillDef.EjectorConnectors) { ejector.CollectAll = true; } }
                        DrillDef.Motor.Enabled = true;
                        DrillDef.Motor.TargetVelocityRPM = drillingRpm;
                        return () => WaitForTicks(30);
                    case 1:
                        DrillDef.Piston.Enabled = true;
                        DrillDef.Piston.Velocity = drillingSpeed;
                        return () => { return DrillDef.Piston.Status == PistonStatus.Extended; };
                    case 2:
                        return () => WaitForTicks(30);
                    case 3:
                        foreach (var drill in DrillDef.ShipDrills) { drill.Enabled = false; }
                        DrillDef.Motor.TargetVelocityRPM = 0;
                        DrillDef.Piston.Velocity = -returnSpeed;
                        return () => { return DrillDef.Piston.Status == PistonStatus.Retracted; };
                    case 4:
                        SequenceStep = -1;
                        SequenceInProgress = false;
                        return () => true;
                    default:
                        return () => false;
                }
            }

            private void RunPeriodicCheck()
            {
                if (SequenceStep == 2 && (periodicCheck++ & 0x3) == 0)
                {
                    if (IsPistonStopped(DrillDef.Motor))
                    {
                        DrillDef.Piston.Velocity = 0;
                    }
                    else if (DrillDef.Sensor.IsActive)
                    {
                        if (periodicCheck >= 12)
                        {
                            periodicCheck = 0;
                            if (CheckCargoFillPercent() < slowSpeedThreshold)
                            {
                                DrillDef.Piston.Velocity = drillingSpeed;
                            }
                            else
                            {
                                DrillDef.Piston.Velocity = drillingSpeedSlow;
                            }
                        }
                    }
                    else
                    {
                        DrillDef.Piston.Velocity = returnSpeed / 2;
                    }
                }
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

            private List<IMyInventory> _cachedCargo;
            private float CheckCargoFillPercent()
            {
                if (_cachedCargo == null) { _cachedCargo = DrillDef.GetDrillInventories().ToList(); }
                float perc = 0;
                foreach (var inv in _cachedCargo)
                {
                    perc += inv.VolumeFillFactor;
                }
                return perc / _cachedCargo.Count;
            }

            private float RotorLastAngle = float.MinValue;
            private bool IsPistonStopped(IMyMotorStator motor)
            {
                const float error = 0.05f;
                float delta;
                if (!(motor.Enabled || motor.TargetVelocityRad == 0)) { RotorLastAngle = float.MinValue; return true; }
                delta = Math.Abs(RotorLastAngle - motor.Angle);
                if (AreSimilar(delta, 0, error)) { return true; }
                RotorLastAngle = motor.Angle;
                return false;
            }
        }
    }
}
