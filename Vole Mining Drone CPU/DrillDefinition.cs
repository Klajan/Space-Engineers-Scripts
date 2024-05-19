using Sandbox.Game.Entities.Cube;
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
using IMyShipDrill = Sandbox.ModAPI.Ingame.IMyShipDrill;
using IMyShipConnector = Sandbox.ModAPI.Ingame.IMyShipConnector;

namespace IngameScript
{
    partial class Program
    {
        public struct DrillDefinition
        {
            private List<IMyShipDrill> _shipDrills;
            public IReadOnlyList<IMyShipDrill> ShipDrills
            {
                get { return _shipDrills.AsReadOnly(); }
            }
            private List<IMyShipConnector> _drillConnectors;
            public IReadOnlyList<IMyShipConnector> EjectorConnectors {
                get { return _drillConnectors.AsReadOnly(); }
            }
            public IMyPistonBase Piston { get; private set; }
            public IMyMotorStator Motor {  get; private set; }
            public IMySensorBlock Sensor { get; private set; }

            public bool IsInitialized { get; private set; }

            public IList<IMyInventory> GetDrillInventories()
            {
                var drillInv = new List<IMyInventory>();
                foreach (var drill in _shipDrills)
                {
                    drillInv.Add(drill.GetInventory());
                }
                return drillInv;
            }

            public static DrillDefinition CreateFromLists(Program program, List<IMyMotorStator> statorList, List<IMyPistonBase> pistonList, List<IMyShipDrill> drillList, List<IMyShipConnector> connectorList, List<IMySensorBlock> sensorList)
            {
                DrillDefinition drill = new DrillDefinition
                {
                    IsInitialized = true
                };
                IMyMotorStator myMotor = statorList.Find(stator => stator.CustomData.Contains("DrillRotor"));
                if(myMotor == null)
                {
                    program.Echo("Could not initalize DrillPiston, no CustomData 'DrillRotor' found!");
                    drill.IsInitialized = false;
                }
                IMyPistonBase myPiston = pistonList.Find(piston => piston.CustomData.Contains("DrillPiston"));
                if (myPiston == null)
                {
                    program.Echo("Could not initalize DrillRotor, no CustomData 'DrillPiston' found!");
                    drill.IsInitialized = false;
                }
                List<IMyShipDrill> myShipDrills = drillList.FindAll(sdrill => sdrill.CustomData.Contains("MiningDrill"));
                if (myShipDrills == null || myShipDrills.Count == 0)
                {
                    program.Echo($"Could not initalize MiningDrills, no CustomData 'MiningDrill' found!");
                    drill.IsInitialized = false;
                }
                List<IMyShipConnector> myShipConnectors = connectorList.FindAll(conn => conn.CustomData.Contains("EjectorConnector"));
                if (myShipConnectors == null || myShipConnectors.Count == 0)
                {
                    program.Echo($"Could not initalize EjectorConnectors, no CustomData 'EjectorConnector' found!");
                    drill.IsInitialized = false;
                }
                IMySensorBlock mySensor = sensorList.Find(sensor => sensor.CustomData.Contains("DrillSensor"));
                if (myPiston == null)
                {
                    program.Echo("Could not initalize DrillSensor, no CustomData 'DrillSensor' found!");
                    drill.IsInitialized = false;
                }

                drill.Piston = myPiston;
                drill.Motor = myMotor;
                drill.Sensor = mySensor;
                drill._shipDrills = myShipDrills;
                drill._drillConnectors = myShipConnectors;
                return drill;
            }
        }
    }
}
