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
            bool TryNextStep();
            void Pack();
            void Unpack();
        }
        public enum MovementDirection : int { Down, Up }
        public class SequenceController : ISequence, ISaveable
        {
            private List<LegMovementController> LegMovementControllers = new List<LegMovementController>();
            private DrillController DrillController; private Func<bool> shouldContinueFunc = () => true;

            #region SaveOnExitVariables     
            public MovementDirection Direction { get; private set; } = MovementDirection.Down;
            public MovementDirection RequestedDirection { get; private set; } = MovementDirection.Down;
            public int SequenceStep { get; private set; } = 0;
            public bool SequenceInProgress { get; private set; } = false;
            private bool waitingForSequence = false;
            public bool IsPacked { get; private set; } = true;
            private int lmcIndex = 0; // Rolling start index of for the LegMovementControllers, should reduce direction bias by alternating starting leg
            public bool IsRunning { get; private set; } = false;
            public bool SequenceEnded { get; private set; } = false;
            #endregion

            public float EjectableCargoVolumeFillFactor { get; set; } = 0;
            public float EjectableCargoFraction { get; set; } = 0;

            #region ISaveable      
            private const int minDataLength = sizeof(int) * 4 + sizeof(bool) * 5;
            public ushort Salt { get { return 0x1004; } }
            public byte[] Serialize()
            {
                byte[] data = BitConverter.GetBytes((int)Direction)
                    .Concat(BitConverter.GetBytes((int)RequestedDirection))
                    .Concat(BitConverter.GetBytes(SequenceStep))
                    .Concat(BitConverter.GetBytes(SequenceInProgress))
                    .Concat(BitConverter.GetBytes(waitingForSequence))
                    .Concat(BitConverter.GetBytes(IsPacked))
                    .Concat(BitConverter.GetBytes(lmcIndex))
                    .Concat(BitConverter.GetBytes(IsRunning))
                    .Concat(BitConverter.GetBytes(SequenceEnded))
                    .ToArray();
                return data;
            }
            public bool Deserialize(byte[] data)
            {
                if (data.Length < minDataLength) return false;
                int index = 0;
                Direction = (MovementDirection)BitConverter.ToInt32(data, index);
                index += sizeof(int);
                RequestedDirection = (MovementDirection)BitConverter.ToInt32(data, index);
                index += sizeof(int);
                SequenceStep = BitConverter.ToInt32(data, index);
                index += sizeof(int);
                SequenceInProgress = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                waitingForSequence = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                IsPacked = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                lmcIndex = BitConverter.ToInt32(data, index);
                index += sizeof(int);
                IsRunning = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                SequenceEnded = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                if (!waitingForSequence && SequenceStep > 0) SequenceStep--; // We were not waiting, so go to previous step
                return true;
            }
            public bool ShouldSerialize()
            {
                // TODO:               
                // Find good indicator when we should serialize   
                // For now we always serialize
                return !IsPacked;
            }
            #endregion

            public SequenceController(LegMovementController fLController, LegMovementController fRController, LegMovementController rLController, LegMovementController rRController, DrillController drillController)
            {
                LegMovementControllers.Add(fLController);
                LegMovementControllers.Add(fRController);
                LegMovementControllers.Add(rRController);
                LegMovementControllers.Add(rLController);
                DrillController = drillController;
            }

            public SequenceController(IList<LegMovementController> LegControllers, DrillController drillController)
            {
                LegMovementControllers = LegControllers.ToList();
                DrillController = drillController;
            }

            private void ResetFields()
            {
                SequenceStep = 0;
                SequenceInProgress = false;
                waitingForSequence = false;
                SequenceEnded = false;
            }

            #region ISequence       
            public bool TryNextStep()
            {
                if (!IsRunning || IsPacked || !shouldContinueFunc()) return false;
                if (Direction == MovementDirection.Down)
                {
                    if (Direction != RequestedDirection && SequenceStep <= 1)
                    {
                        ResetFields();
                        Direction = RequestedDirection;
                        DrillController.Pack();
                        foreach (var lmc in LegMovementControllers)
                        {
                            lmc.SwitchDirection(MovementDirection.Up);
                            lmc.RestartSequence();
                        }
                        shouldContinueFunc = TrueFunc;
                        return true;
                    }
                    shouldContinueFunc = DescentSequence();
                }
                else
                {
                    if (Direction != RequestedDirection && SequenceStep >= 2)
                    {
                        ResetFields();
                        Direction = RequestedDirection;
                        DrillController.Unpack();
                        foreach (var lmc in LegMovementControllers)
                        {
                            lmc.SwitchDirection(MovementDirection.Down);
                            lmc.RestartSequence();
                        }
                        shouldContinueFunc = TrueFunc;
                        return true;
                    }
                    shouldContinueFunc = AscendSequence();
                }
                if (SequenceEnded) { ResetFields(); }
                else if (!waitingForSequence) { SequenceStep++; }
                return true;
            }
            #endregion     

            #region commands
            public bool IsSafeToPack { get { return !IsRunning || SequenceEnded; } }
            public void Pack()
            {
                DrillController.Pack();
                LegMovementControllers.ForEach((lmc) => lmc.Pack());
                IsPacked = true;
                IsRunning = false;
                SequenceStep = 0;
            }

            public bool IsSafeToUnpack { get { return !IsRunning || IsPacked; } }
            public void Unpack()
            {
                DrillController.Unpack();
                LegMovementControllers.ForEach((lmc) => lmc.Unpack());
                IsPacked = false;
                IsRunning = false;
                SequenceStep = 0;
            }

            public void RequestDirection(MovementDirection direction)
            {
                IsRunning = true;
                RequestedDirection = direction;
            }

            public void Stop()
            {
                IsRunning = false;
            }
            #endregion
            private Func<bool> DescentSequence()
            {
                switch (SequenceStep)
                {
                    case 0:
                        SequenceInProgress = true;
                        SequenceEnded = false;
                        DrillController.TryNextStep();
                        waitingForSequence = DrillController.SequenceInProgress;
                        return TrueFunc;
                    case 1:
                        return () => EjectableCargoFraction <= 0.3 || EjectableCargoVolumeFillFactor <= 0.45;
                    case 2:
                        waitingForSequence = TryParallelMovementStep();
                        return TrueFunc;
                    case 3:
                        lmcIndex = (lmcIndex + 1) % LegMovementControllers.Count; // Advance start Index to avoid direction bias
                        LegMovementControllers.ForEach(itm => itm.RestartSequence());
                        SequenceEnded = true;
                        return TrueFunc;
                    default:
                        return TrueFunc;
                }
            }
            private Func<bool> AscendSequence()
            {
                switch (SequenceStep)
                {
                    case 0:
                        SequenceInProgress = true;
                        SequenceEnded = false;
                        waitingForSequence = TryParallelMovementStep();
                        return TrueFunc;
                    case 1:
                        lmcIndex = (lmcIndex + 1) % LegMovementControllers.Count; // Advance start Index to avoid direction bias
                        var isDone = true;
                        LegMovementControllers.ForEach(lmc => { isDone &= lmc.HasAscended; });
                        if (isDone)
                        {
                            EchoDebug("IsAscended");
                            IsRunning = false;
                        }
                        LegMovementControllers.ForEach(lmc => lmc.RestartSequence());
                        return TrueFunc;
                    case 2:
                        SequenceEnded = true;
                        return TrueFunc;
                    default:
                        return TrueFunc;
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

            private bool TryParallelMovementStep()
            {
                bool allLegsMoveNext = true;
                bool inProgress = false;
                int i;
                bool doNextLeg = true;
                for (i = 0; i < LegMovementControllers.Count; i = i + 2)
                {
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
                if (allLegsMoveNext && i >= LegMovementControllers.Count)
                {
                    LegMovementControllers.ForEach(lmc => lmc.ContinueSequence());
                }

                return inProgress;
            }

            private bool TrueFunc() { return true; }
        }
    }
}