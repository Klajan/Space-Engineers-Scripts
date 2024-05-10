using Sandbox.Game.Entities.Blocks;
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
    partial class Program : MyGridProgram
    {
        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        // 
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.

        private LegMovementController LegControllerFL;
        private LegMovementController LegControllerFR;
        private LegMovementController LegControllerRL;
        private LegMovementController LegControllerRR;
        private DrillController MainDrillController;

        private SequenceController sequenceController;

        private PersistantStorage persistantStorage;

        public Program()
        {
            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script. 
            //     
            // The constructor is optional and can be removed if not
            // needed.
            // 
            // It's recommended to set Runtime.UpdateFrequency 
            // here, which will allow your script to run itself without a 
            // timer block.
            persistantStorage = new PersistantStorage(this);
            var rotors = new List<IMyMotorStator>();
            GridTerminalSystem.GetBlocksOfType(rotors, block => block.IsSameConstructAs(Me));
            var pistons = new List<IMyPistonBase>();
            GridTerminalSystem.GetBlocksOfType(pistons, block => block.IsSameConstructAs(Me));
            var sensors = new List<IMySensorBlock>();
            GridTerminalSystem.GetBlocksOfType(sensors, block => block.IsSameConstructAs(Me));
            var landingGears = new List<IMyLandingGear>();
            GridTerminalSystem.GetBlocksOfType(landingGears, block => block.IsSameConstructAs(Me));
            var drills = new List<IMyShipDrill>();
            GridTerminalSystem.GetBlocksOfType(drills, block => block.IsSameConstructAs(Me));
            var connectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(connectors, block => block.IsSameConstructAs(Me));

            var flLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.FrontLeft, rotors, pistons, sensors, landingGears);
            var frLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.FrontRight, rotors, pistons, sensors, landingGears);
            var rlLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.RearLeft, rotors, pistons, sensors, landingGears);
            var rrLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.RearRight, rotors, pistons, sensors, landingGears);
            var mainDrill = DrillDefinition.CreateFromLists(this, rotors, pistons, drills, connectors);
            
            
            if (!flLeg.IsInitialized | !frLeg.IsInitialized | !rlLeg.IsInitialized | !rrLeg.IsInitialized | !mainDrill.IsInitialized) return;

            MainDrillController = new DrillController(mainDrill);
            LegControllerFL = new LegMovementController(flLeg);
            LegControllerFR = new LegMovementController(frLeg);
            LegControllerRL = new LegMovementController(rlLeg);
            LegControllerRR = new LegMovementController(rrLeg);
            sequenceController = new SequenceController(this, LegControllerFL, LegControllerFR, LegControllerRL, LegControllerRR);

            persistantStorage.Register(LegControllerFL);
            persistantStorage.Register(LegControllerFR);
            persistantStorage.Register(LegControllerRL);
            persistantStorage.Register(LegControllerRR);
            persistantStorage.Register(sequenceController);
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        public void Main(string argument, UpdateType updateSource)
        {
            // The main entry point of the script, invoked every time
            // one of the programmable block's Run actions are invoked,
            // or the script updates itself. The updateSource argument
            // describes where the update came from. Be aware that the
            // updateSource is a  bitfield  and might contain more than 
            // one update type.
            // 
            // The method itself is required, but the arguments above
            // can be removed if not needed.
            sequenceController.Pack();
            //sequenceController.Unpack();
        }

        public static bool AreSimilar(float A, float B, float delta = 0.0001f)
        {
            return (Math.Abs(A - B) < delta);
        }
        public static bool AreSimilar(double A, double B, double delta = 0.0001)
        {
            return (Math.Abs(A - B) < delta);
        }

    }
}
