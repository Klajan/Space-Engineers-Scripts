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
            #endregion

            public DrillController(DrillDefinition drill, float drillingSpeed = 0.05f, float drillingRpm = 3)
            {
                this.DrillDef = drill;
                this.drillingSpeed = drillingSpeed;
                this.drillingSpeedSlow = drillingSpeed / 3;
                this.drillingRpm = drillingRpm;
            }

            #region ISavable
            private const int minDataLength = sizeof(int) * 1 + sizeof(bool) * 1;
            public bool ShouldSerialize()
            {
                return true;
            }
            public byte[] Serialize()
            {
                byte[] data = BitConverter.GetBytes(SequenceStep)
                    .Concat(BitConverter.GetBytes(SequenceInProgress))
                    .Concat(BitConverter.GetBytes(EnableEjectors))
                    .Concat(BitConverter.GetBytes(ticksWaited))
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
                return true;
            }
            #endregion

            #region ISequence
            public UpdateFrequency MyUpdateFrequency { get; private set; } = UpdateFrequency.Update100;

            public bool TryNextStep()
            {
                if(SequenceStep < 3 && periodicCheck++ >= 20)
                {
                    EchoDebug($"Percent Filled: {CheckCargoFillPercent()}");
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
                SequenceStep = 0;
            }

            public void Unpack()
            {
                DrillDef.Motor.Enabled = true;
                DrillDef.Motor.TargetVelocityRPM = 0;
                DrillDef.Motor.UpperLimitRad = float.MaxValue;
                DrillDef.Motor.LowerLimitDeg = float.MinValue;
                SequenceStep = 0;
            }

            #endregion

            private Func<bool> RunSequence()
            {
                switch (SequenceStep)
                {
                    case 0:
                        //EchoDebug("Case 0");
                        SequenceInProgress = true;
                        foreach (var drill in DrillDef.ShipDrills) { drill.Enabled = true; }
                        if (EnableEjectors) { foreach (var ejector in DrillDef.EjectorConnectors) { ejector.CollectAll = true; } }
                        DrillDef.Motor.Enabled = true;
                        DrillDef.Motor.TargetVelocityRPM = drillingRpm;
                        return () => WaitForTicks(30);
                    case 1:
                        //EchoDebug("Case 1");
                        DrillDef.Piston.Enabled = true;
                        DrillDef.Piston.Velocity = drillingSpeed;
                        return () => { return DrillDef.Piston.Status == PistonStatus.Extended; };
                    case 2:
                        //EchoDebug("Case 2");
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

            private bool WaitForTicks(int ticksToWait)
            {
                ticksWaited++;
                //EchoDebug($"Waited for: {ticksWaited}");
                if ( ticksWaited > ticksToWait)
                {
                    ticksWaited = 0;
                    return true;
                }
                return false;
            }

            private List<IMyInventory> _cachedCargo;
            private float CheckCargoFillPercent()
            {
                if( _cachedCargo == null ) { _cachedCargo = DrillDef.GetDrillInventories().ToList(); }
                float perc = 0;
                foreach (var inv in _cachedCargo)
                {
                    perc += inv.VolumeFillFactor;
                }
                return perc / _cachedCargo.Count;
            }
        }
    }
}
