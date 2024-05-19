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
using IMyShipController = Sandbox.ModAPI.Ingame.IMyShipController;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        const float DrillSpeed = 0.04f;
        const float DrillRPM = 3f;
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

        private ProgramStorage Vars;

        private LegMovementController LegControllerFL;
        private LegMovementController LegControllerFR;
        private LegMovementController LegControllerRL;
        private LegMovementController LegControllerRR;
        private DrillController MainDrillController;

        private StorageMonitor storageMonitor;
        private SequenceController sequenceController;

        private PersistantStorage persistantStorage;

        private MyCommandLine _commandLine = new MyCommandLine();
        private Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        private SimpleMovingAverage runtimeAvg = new SimpleMovingAverage(100);

        private bool _inventoryFull = false;
        private IMyShipController ShipController;

        public Program()
        {
            Vars = new ProgramStorage(this);
            _debugEcho = Echo;
            /*
             * Get all of the required blocks on the ship 
             */
            bool IsInitialized = true;
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
            var storages = new List<IMyCargoContainer>();
            GridTerminalSystem.GetBlocksOfType(storages, block => block.IsSameConstructAs(Me));
            if (storages == null || storages.Count == 0)
            {
                IsInitialized = false;
                Echo("No CargoContainers found!");
            }
            var shipControllers = new List<IMyShipController>();
            GridTerminalSystem.GetBlocksOfType(shipControllers, block => block.IsSameConstructAs(Me));
            if (storages == null || storages.Count == 0)
            {
                IsInitialized = false;
                Echo("No ShipController found!");
            }

            ShipController = shipControllers[0];
            var flLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.FrontLeft, rotors, pistons, sensors, landingGears);
            var frLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.FrontRight, rotors, pistons, sensors, landingGears);
            var rlLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.RearLeft, rotors, pistons, sensors, landingGears);
            var rrLeg = LegDefinition.CreateFromLists(this, LegDefinition.LegLocation.RearRight, rotors, pistons, sensors, landingGears);
            var mainDrill = DrillDefinition.CreateFromLists(this, rotors, pistons, drills, connectors, sensors);


            if (!IsInitialized || !flLeg.IsInitialized | !frLeg.IsInitialized | !rlLeg.IsInitialized | !rrLeg.IsInitialized | !mainDrill.IsInitialized) return;

            MainDrillController = new DrillController(mainDrill, DrillSpeed, DrillRPM);
            LegControllerFL = new LegMovementController(flLeg);
            LegControllerFR = new LegMovementController(frLeg);
            LegControllerRL = new LegMovementController(rlLeg);
            LegControllerRR = new LegMovementController(rrLeg);
            sequenceController = new SequenceController(LegControllerFL, LegControllerFR, LegControllerRL, LegControllerRR, MainDrillController);
            storageMonitor = new StorageMonitor(storages);
            storageMonitor.RegisterCargo(MainDrillController.DrillDef.GetDrillInventories());

            persistantStorage.Register(Vars);
            persistantStorage.Register(LegControllerFL);
            persistantStorage.Register(LegControllerFR);
            persistantStorage.Register(LegControllerRL);
            persistantStorage.Register(LegControllerRR);
            persistantStorage.Register(sequenceController);
            persistantStorage.Register(MainDrillController);

            RegisterCommands();

            if (this.Storage != string.Empty)
            {
                if (persistantStorage.DeserializeAll(this.Storage))
                {
                    Echo("Successfully loaded previous session!");
                }
            }

            //this.Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 

            this.Storage = persistantStorage.SerializeAll();
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
            if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0)
            {
                RunDefault(argument);
            }
            if ((updateSource & UpdateType.Update10) != 0)
            {
                if (sequenceController.IsRunning)
                {
                    sequenceController.TryNextStep();
                }
            }
            if ((updateSource & UpdateType.Update100) != 0)
            {
                InventoryCheck();
                DepthCheck();
                //Echo($"Last RunTime: {this.Runtime.LastRunTimeMs}ms");
                //Echo($"Average RunTime: {runtimeAvg.Update(this.Runtime.LastRunTimeMs)}ms");
            }
        }

        internal void RunDefault(string argument)
        {
            if (_commandLine.TryParse(argument))
            {
                Action commandAction;

                // Retrieve the first argument. Switches are ignored.
                string command = _commandLine.Argument(0);

                // Now we must validate that the first argument is actually specified, 
                // then attempt to find the matching command delegate.
                if (command == null)
                {
                    Echo("No command specified");
                }
                else if (_commands.TryGetValue(command, out commandAction))
                {
                    // We have found a command. Invoke it.
                    commandAction();
                }
                else
                {
                    Echo($"Unknown command {command}");
                }
            }
        }

        #region Commands
        private void PackCommand()
        {
            if( sequenceController.IsSafeToPack || _commandLine.Switch("force"))
            {
                sequenceController.Pack();
                Vars.UpdateFrequency &= ~UpdateFrequency.Update10;
            }
            else
            {
                Echo("Unsafe to Pack, use -force switch to force!");
            }
        }

        private void UnpackCommand()
        {
            if (sequenceController.IsSafeToUnpack || _commandLine.Switch("force"))
            {
                sequenceController.Unpack();
                Vars.UpdateFrequency |= UpdateFrequency.Update10 | UpdateFrequency.Update100;
            } else
            {
                Echo("Unsafe to Unpack, use -force switch to force!");
            }
        }

        private void SetDepthCommand()
        {
            const int addValue = 25;
            if (_commandLine.Items.Count >= 2)
            {
                var arg = _commandLine.Items[1];
                if (arg == "+")
                {
                    Vars.MaxDrillingDepth += addValue;
                }
                else if (arg == "-")
                {
                    Vars.MaxDrillingDepth -= addValue;
                }
                else
                {
                    int val = 100;
                    if (int.TryParse(arg, out val)) Vars.MaxDrillingDepth = val;
                }
                Echo($"Max Drilling Depth: {Vars.MaxDrillingDepth}");
            }
        }

        private void StartUpdatesCommand()
        {
            var allSwitch = _commandLine.Switch("-all");
            var update = UpdateFrequency.Update100;
            if (allSwitch)
            {
                update |= UpdateFrequency.Update10;
            }
            Vars.UpdateFrequency |= update;
        }
        #endregion

        private void RegisterCommands()
        {
            _commands.Add("Pack", PackCommand);
            _commands.Add("Unpack", UnpackCommand);
            _commands.Add("Stop", sequenceController.Stop);

            _commands.Add("StartDrilling", () => sequenceController.RequestDirection(MovementDirection.Down));
            _commands.Add("StopDrilling", () => sequenceController.RequestDirection(MovementDirection.Up));
            _commands.Add("Descend", () => sequenceController.RequestDirection(MovementDirection.Down));
            _commands.Add("Ascend", () => sequenceController.RequestDirection(MovementDirection.Up));

            _commands.Add("SetMaxDepth", SetDepthCommand);
            _commands.Add("StopUpdates", () => { Vars.UpdateFrequency = UpdateFrequency.None; });
            _commands.Add("StartUpdates", StartUpdatesCommand);

            _commands.Add("loadnow", () => persistantStorage.DeserializeAll(this.Storage));
            _commands.Add("savenow", () => this.Storage = persistantStorage.SerializeAll());
            _commands.Add("resetstorage", () => this.Storage = String.Empty);
        }

        private void InventoryCheck()
        {
            _inventoryFull = storageMonitor.CheckInventoryFull();
            sequenceController.EjectableCargoFraction = storageMonitor.GetBlacklistCargoFraction();
            sequenceController.EjectableCargoVolumeFillFactor = storageMonitor.GetBlacklistVolumeFillFactor();
            if (_inventoryFull)
            {
                if (sequenceController.IsRunning && sequenceController.RequestedDirection == MovementDirection.Down)
                {
                    sequenceController.RequestDirection(MovementDirection.Up);
                }
            }
        }

        private void DepthCheck()
        {
            double elevation = 0;
            if (ShipController.TryGetPlanetElevation(MyPlanetElevation.Surface, out elevation))
            {
                if (sequenceController.IsRunning && sequenceController.RequestedDirection == MovementDirection.Down && elevation > Vars.MaxDrillingDepth)
                {
                    Echo($"Elevation: {elevation}");
                    sequenceController.RequestDirection(MovementDirection.Up);
                }
            }
        }

        

        #region Helper Functions

        public static bool AreSimilar(float A, float B, float delta = 0.0001f)
        {
            return (Math.Abs(A - B) < delta);
        }

        public static bool AreSimilar(double A, double B, double delta = 0.0001)
        {
            return (Math.Abs(A - B) < delta);
        }

        public static float RadiansToDegrees(float angle) => angle * (180.0f / (float)Math.PI);

        public static float DegreesToRadians(float angle) => angle * ((float)Math.PI / 180.0f);

        private static float Lerp(float from, float to, float by)
        {
            return from * (1 - by) + to * by;
        }

        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        private static Action<string> _debugEcho = (message) => { };

        public static void EchoDebug(string message)
        {
            _debugEcho(message);
        }

        #endregion

    }
}
