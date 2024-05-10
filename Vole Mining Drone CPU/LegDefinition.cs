using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
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
using IMyMotorStator = Sandbox.ModAPI.Ingame.IMyMotorStator;
using IMyPistonBase = Sandbox.ModAPI.Ingame.IMyPistonBase;
using IMySensorBlock = Sandbox.ModAPI.Ingame.IMySensorBlock;

namespace IngameScript
{
    partial class Program
    {
        public struct LegDefinition
        {
            public enum LegLocation
            {
                FrontLeft,
                FrontRight,
                RearLeft,
                RearRight
            }

            public IMyMotorStator HipHingeHoriz { get; private set; }
            public IMyMotorStator HipHingeVert { get; private set; }
            public IMyMotorStator KneeHinge { get; private set; }
            public IMyMotorStator FootHinge { get; private set; }
            public IMyPistonBase UpperLegPiston { get; private set; }
            public IMyPistonBase LowerLegPiston { get; private set; }
            public IMySensorBlock LegSenor { get; private set; }
            private List<IMyLandingGear> _legMagnets;
            public IReadOnlyList<IMyLandingGear> LegMagnets
            {
                get { return _legMagnets.AsReadOnly(); }
            }

            public LegLocation Location { get; private set; }

            public bool IsInitialized { get; private set; }

            public static LegDefinition CreateFromLists(Program program, LegLocation location, List<IMyMotorStator> statorList, List<IMyPistonBase> pistonList, List<IMySensorBlock> sensorList, List<IMyLandingGear> landingGearList)
            {
                // Some Helpers to generate CustomData search Keys
                string[] prefixStrings = { "FL", "FR", "RL", "RR" };
                Func<string, string> makeKey = (string key) =>
                {
                    return String.Concat(prefixStrings[(int)location], key);
                };

                LegDefinition leg = new LegDefinition
                {
                    IsInitialized = true
                };
                // Try to find the CustomData that defines the Hinges & Pistons

                IMyMotorStator hipH = statorList.Find(stator => stator.CustomData == makeKey("HipHingeH"));
                if (hipH == null)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} HipHingeHorizontal, no CustomData found!");
                    leg.IsInitialized = false;
                }
                IMyMotorStator hipV = statorList.Find(stator => stator.CustomData == makeKey("HipHingeV"));
                if (hipV == null)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} HipHingeVertical, no CustomData found!");
                    leg.IsInitialized = false;
                }
                IMyMotorStator knee = statorList.Find(stator => stator.CustomData == makeKey("KneeHinge"));
                if (knee == null)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} KneeHinge, no CustomData found!");
                    leg.IsInitialized = false;
                }
                IMyMotorStator foot = statorList.Find(stator => stator.CustomData == makeKey("FootHinge"));
                if (foot == null)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} FootHinge, no CustomData found!");
                    leg.IsInitialized = false;
                }
                IMyPistonBase uLeg = pistonList.Find(piston => piston.CustomData == makeKey("UpperLegPiston"));
                if (uLeg == null)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} UpperLegPiston, no CustomData found!");
                    leg.IsInitialized = false;
                }
                IMyPistonBase lLeg = pistonList.Find(piston => piston.CustomData == makeKey("LowerLegPiston"));
                if (lLeg == null)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} LowerLegPiston, no CustomData found!");
                    leg.IsInitialized = false;
                }
                IMySensorBlock sense = sensorList.Find(sensor => sensor.CustomData == makeKey("LegSensor"));
                if (sense == null)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} LegSensor, no CustomData found!");
                    leg.IsInitialized = false;
                }
                List<IMyLandingGear> landingGears = landingGearList.FindAll(magnet => magnet.CustomData == makeKey("LegLandingGear"));
                if (landingGears == null || landingGears.Count == 0)
                {
                    program.Echo($"Could not initalize {prefixStrings[(int)location]} LegLandingGear, no CustomData found!");
                    leg.IsInitialized = false;
                }

                leg.HipHingeHoriz = hipH;
                leg.HipHingeVert = hipV;
                leg.KneeHinge = knee;
                leg.FootHinge = foot;
                leg.UpperLegPiston = uLeg;
                leg.LowerLegPiston = lLeg;
                leg.LegSenor = sense;
                leg.Location = location;
                leg._legMagnets = landingGears;
                return leg;
            }
        }
    }
}
