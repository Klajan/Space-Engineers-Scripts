using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;
using VRageMath;
namespace IngameScript
{
    partial class Program
    {
        internal interface ISequence
        {
            UpdateFrequency MyUpdateFrequency { get; }
            bool TryNextStep();
            void Pack();
            void Unpack();
        }
        public enum MovementDirection : int { Down, Up }
        public class SequenceController : ISequence, ISaveable
        {
            private Program _program;
            private List<LegMovementController> LegMovementControllers = new List<LegMovementController>();
            private DrillController DrillController; private Func<bool> _shouldContinueFunc = () => true;

            #region SaveOnExitVariables     
            public MovementDirection Direction { get; private set; } = MovementDirection.Down;
            public int SequenceStep { get; private set; } = 0;
            public bool SequenceInProgress { get; private set; } = false;
            private bool waitingForSequence = false;
            public bool IsPacked { get; private set; } = true;
            private int lmcIndex = 0; // Rolling start index of for the LegMovementControllers, should reduce direction bias by alternating starting leg
            public bool IsRunning { get; private set; } = false;
            #endregion

            private MovementDirection cachedDirection = MovementDirection.Down;
            public float EjectableCargoVolumeFillFactor { get; set; } = 0;
            public float EjectableCargoFraction { get; set; } = 0;

            #region ISaveable      
            private const int minDataLength = sizeof(int) * 3 + sizeof(bool) * 4;
            public byte[] Serialize()
            {
                byte[] data = BitConverter.GetBytes((int)Direction)
                    .Concat(BitConverter.GetBytes(SequenceStep))
                    .Concat(BitConverter.GetBytes(SequenceInProgress))
                    .Concat(BitConverter.GetBytes(waitingForSequence))
                    .Concat(BitConverter.GetBytes(IsPacked))
                    .Concat(BitConverter.GetBytes(lmcIndex))
                    .Concat(BitConverter.GetBytes(IsRunning))
                    .ToArray();
                return data;
            }
            public bool Deserialize(byte[] data)
            {
                if (data.Length < minDataLength) return false;
                int index = 0;
                Direction = (MovementDirection)BitConverter.ToInt32(data, index);
                index += sizeof(int);
                SequenceStep = BitConverter.ToInt32(data, index);
                index += sizeof(int);
                SequenceInProgress = BitConverter.ToBoolean(data, index);
                index += sizeof(bool); waitingForSequence = BitConverter.ToBoolean(data, index);
                index += sizeof(bool); IsPacked = BitConverter.ToBoolean(data, index);
                index += sizeof(bool); lmcIndex = BitConverter.ToInt32(data, index);
                index += sizeof(int); IsRunning = BitConverter.ToBoolean(data, index);
                index += sizeof(bool); if (!waitingForSequence && SequenceStep > 0) SequenceStep--; // We were not waiting, so go to previous step
                cachedDirection = Direction;
                return true;
            }
            public bool ShouldSerialize()
            {
                // TODO:               
                // Find good indicator when we should serialize   
                // For now we always serialize
                return true;
            }
            #endregion
            public SequenceController(Program program, LegMovementController fLController, LegMovementController fRController, LegMovementController rLController, LegMovementController rRController, DrillController drillController)
            {
                _program = program;
                LegMovementControllers.Add(fLController);
                LegMovementControllers.Add(fRController);
                LegMovementControllers.Add(rRController);
                LegMovementControllers.Add(rLController);
                DrillController = drillController;
            }
            #region ISequence       
            public UpdateFrequency MyUpdateFrequency { get; private set; } = UpdateFrequency.Update100;
            public bool TryNextStep()
            {
                //EchoDebug((!IsRunning || IsPacked || !_shouldContinueFunc()).ToString());
                if (!IsRunning || IsPacked || !_shouldContinueFunc()) return false;

                if (cachedDirection == MovementDirection.Down)
                {
                    if (cachedDirection != Direction) // requested direction switch;
                    {
                        if (SequenceStep != 1)
                        {
                            SequenceStep = 0;
                            cachedDirection = MovementDirection.Up;
                            DrillController.Pack();
                            foreach (var lmc in LegMovementControllers)
                            {
                                lmc.SwitchDirection(MovementDirection.Up);
                            }
                            return false;
                        }
                    }
                    else
                    {
                        _shouldContinueFunc = DescentSequence();
                    }
                }
                else
                {
                    if (cachedDirection != Direction) // requested direction switch;
                    {
                        if (SequenceStep != 0)
                        {
                            SequenceStep = 0;
                            cachedDirection = MovementDirection.Down;
                            DrillController.Unpack();
                            foreach (var lmc in LegMovementControllers)
                            {
                                lmc.SwitchDirection(MovementDirection.Down);
                            }
                        }
                    }
                    else
                    {
                        _shouldContinueFunc = AscendSequence();
                    }
                }
                //EchoDebug($"Waiting: {waitingForSequence}");
                if (!waitingForSequence) { SequenceStep++; }
                return true;
            }
            #endregion     

            #region commands   
            public void Pack()
            {
                DrillController.Pack();
                LegMovementControllers.ForEach((lmc) => lmc.Pack());
                IsPacked = true;
                IsRunning = false;
                SequenceStep = 0;
            }
            public void Unpack()
            {
                DrillController.Unpack();
                LegMovementControllers.ForEach((lmc) => lmc.Unpack());
                IsPacked = false;
                IsRunning = false;
                SequenceStep = 0;
            }
            public void StartAscend()
            {
                IsRunning = true;
                Direction = MovementDirection.Up;
            }
            public void StartDescend()
            {
                IsRunning = true;
                Direction = MovementDirection.Down;
            }
            public void Stop()
            {
                IsRunning = false;
            }
            #endregion
            private Func<bool> DescentSequence()
            {
                SequenceInProgress = true;
                switch (SequenceStep)
                {
                    case 0:
                        //EchoDebug("Running Drilling Sequence");
                        DrillController.TryNextStep();
                        waitingForSequence = DrillController.SequenceInProgress;
                        return TrueFunc;
                    case 1:
                        //EchoDebug("Running Movement Sequence");
                        if (EjectableCargoFraction >= 0.3 && EjectableCargoVolumeFillFactor >= 0.5)
                        {
                            waitingForSequence = true;
                            return () => EjectableCargoFraction <= 0.3 || EjectableCargoVolumeFillFactor <= 0.5;
                        }
                        waitingForSequence = TryMovementStep_New();
                        return TrueFunc;
                    case 2:
                        lmcIndex = (lmcIndex + 1) % LegMovementControllers.Count; // Advance start Index to avoid direction bias
                        LegMovementControllers.ForEach(itm => itm.RestartSequence());
                        SequenceStep = -1;
                        return TrueFunc;
                    default:
                        return () => false;
                }
            }
            private Func<bool> AscendSequence()
            {
                switch (SequenceStep)
                {
                    case 0:
                        //EchoDebug("Running Movement Sequence");
                        waitingForSequence = TryMovementStep_New();
                        return TrueFunc;
                    case 1:
                        lmcIndex = (lmcIndex + 1) % LegMovementControllers.Count; // Advance start Index to avoid direction bias
                        var isDone = true;
                        LegMovementControllers.ForEach(itm => { isDone &= itm.HasAscended; });
                        LegMovementControllers.ForEach(itm => itm.RestartSequence());
                        SequenceStep = -1;
                        if (isDone)
                        {
                            IsRunning = false;
                        }
                        return TrueFunc;
                    default:
                        return () => false;
                }
            }
            // Returns true if Sequence is still running
            private bool TryMovementStep()
            {
                bool allLegsMoveNext = true;
                bool inProgress = false;
                int i;
                bool doNextLeg = true;
                for (i = 0; i < LegMovementControllers.Count; i++)
                {
                    if (doNextLeg)
                    {
                        //EchoDebug($"Leg {lmcIndex}");
                        LegMovementControllers[lmcIndex].TryNextStep();
                    }
                    doNextLeg = LegMovementControllers[lmcIndex].ShouldMoveNextLeg;
                    allLegsMoveNext &= LegMovementControllers[lmcIndex].IsWaitingForContinue;
                    inProgress |= LegMovementControllers[lmcIndex].SequenceInProgress;
                    lmcIndex = (lmcIndex + 1) % LegMovementControllers.Count;
                }
                if (allLegsMoveNext && i == LegMovementControllers.Count) LegMovementControllers.ForEach(lmc => lmc.ContinueSequence());
                return inProgress;
            }

            private bool TryMovementStep_New()
            {
                bool allLegsMoveNext = true;
                bool inProgress = false;
                int i;
                bool doNextLeg = true;
                for (i = 0; i < LegMovementControllers.Count; i = i + 2)
                {
                    EchoDebug($"Leg {lmcIndex}");
                    int index2 = (lmcIndex + 2) % LegMovementControllers.Count;
                    if (doNextLeg)
                    {
                        LegMovementControllers[lmcIndex].TryNextStep();
                        LegMovementControllers[index2].TryNextStep();
                    }
                    doNextLeg &= LegMovementControllers[lmcIndex].ShouldMoveNextLeg;
                    doNextLeg &= LegMovementControllers[index2].ShouldMoveNextLeg;
                    allLegsMoveNext &= LegMovementControllers[lmcIndex].IsWaitingForContinue;
                    allLegsMoveNext &= LegMovementControllers[index2].IsWaitingForContinue;
                    inProgress |= LegMovementControllers[lmcIndex].SequenceInProgress;
                    inProgress |= LegMovementControllers[index2].SequenceInProgress;
                    lmcIndex = (lmcIndex + 1) % LegMovementControllers.Count;
                }
                if (allLegsMoveNext && i >= LegMovementControllers.Count) LegMovementControllers.ForEach(lmc => lmc.ContinueSequence());
                return inProgress;
            }

            private bool TrueFunc() { return true; }
        }
    }
}