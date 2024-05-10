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
        public class SequenceController : ISaveable
        {
            private Program _program;

            private List<LegMovementController> LegMovementControllers = new List<LegMovementController>();

            private Func<bool> shouldContinueFunc = () => true;
            private bool goingDown = false;

            #region SaveOnExitVariables
            public int SequenceStep { get; private set; } = 0;
            public bool SequenceInProgress { get; private set; } = false;
            public bool IsDefaultState { get; private set; } = true;
            #endregion

            #region ISaveable
            const char seperator = '\u0091'; // Private Use Unciode Control Character
            public string Serialize()
            {
                return string.Join(seperator.ToString(), SequenceStep, SequenceInProgress);
            }
            public bool Deserialize(string dataString)
            {
                return true;
            }
            public bool ShouldSerialize()
            {
                return IsDefaultState;
            }
            #endregion

            public SequenceController(Program program, LegMovementController fLController, LegMovementController fRController, LegMovementController rLController, LegMovementController rRController)
            {
                _program = program;
                LegMovementControllers.Add(fLController);
                LegMovementControllers.Add(fRController);
                LegMovementControllers.Add(rLController);
                LegMovementControllers.Add(rRController);
            }

            public bool TryNextStep()
            {
                if (!shouldContinueFunc()) return false;
                if (goingDown)
                {
                    shouldContinueFunc = DescentSequence();
                    SequenceStep++;
                    return true;
                }
                else
                {
                    return false;
                }

            }

            public void Pack()
            {
                LegMovementControllers.ForEach((lmc) => lmc.Pack());
            }
            public void Unpack()
            {
                LegMovementControllers.ForEach((lmc) => lmc.Unpack());
            }

            private Func<bool> DescentSequence()
            {
                SequenceInProgress = true;
                switch (SequenceStep)
                {
                    case 0:

                    default:
                        break;
                }
                tryMovementStep();
                return () => true;
            }

            private void tryMovementStep()
            {
                bool allLegsMoveNext = false;
                bool doLoop = true;
                for (int i = 0; i < LegMovementControllers.Count && doLoop; i++)
                {
                    do
                    {
                        if (!LegMovementControllers[i].ShouldMoveNextLeg) doLoop = false;
                        allLegsMoveNext &= LegMovementControllers[i].ShouldMoveNextLeg;
                    } while (LegMovementControllers[i].TryMoveNext());
                }
                if (allLegsMoveNext) LegMovementControllers.ForEach(mvCtr => mvCtr.Enable());
            }
        }
    }
}
