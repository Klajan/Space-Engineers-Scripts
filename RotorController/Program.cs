using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
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
//using static IngameScript.Program;
//using static VRageRender.Messages.MyRenderMessageUpdateComponent;
using UpdateType = Sandbox.ModAPI.Ingame.UpdateType;
using IMyMotorStator = Sandbox.ModAPI.Ingame.IMyMotorStator;

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


        // PLEASE DO NOT EDIT BELOW----------------------------------------------------------------------------------------------------------------------------------- 

        MyCommandLine _commandLine = new MyCommandLine();
        MyConfiguration _configuration = new MyConfiguration();

        private readonly List<Rotor> _listRotors = new List<Rotor>();
        private readonly Dictionary<string, Rotor> _rotorDict = new Dictionary<string, Rotor>();
        private readonly LinkedList<Rotor> _cleanupList = new LinkedList<Rotor>();

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

            if (!_configuration.ReadConfig(Me.CustomData))
            {
                Echo("Error Reading Configuration from CustomData.\n");
                Echo("Please check that the config is present in the expected format:\n");
                Echo(_configuration.ErrorString);
                return;
            }

            List<IMyMotorStator> motorStators = new List<IMyMotorStator>();
            GridTerminalSystem.GetBlocksOfType(motorStators, (stator) =>
            {
                return _configuration.RotorNames.Exists((stringToCheck) =>
                {
                    return stringToCheck.Equals(stator.CustomName);
                }); 
            });

            if (motorStators.Count != _configuration.RotorNames.Count)
            {
                Echo("Not all rotors were found, please check for missing or duplicates:\n");
                foreach (var motor in motorStators) { Echo($"{motor.CustomName}\n"); }
            }

            foreach (var motor in motorStators)
            {
                _rotorDict.Add(motor.CustomName, new Rotor(motor, (float)Math.Abs(_configuration.DefaultVelocity), motor.LowerLimitRad, motor.UpperLimitRad));
                Echo($"Added Rotor: '{motor.CustomName}'");
            }

            Echo($"Configured {_rotorDict.Count} Rotors.");

            //foreach (string name in _configuration.RotorNames)
            //{
            //    IMyMotorStator rotor = GridTerminalSystem.GetBlockWithName(name) as IMyMotorStator;
            //    if (rotor == null)
            //    {
            //        Echo($"No rotor found with name: {name}\n");
            //        Echo("Please correct naming and recompile!\n");
            //    }

            //    _rotorDict.Add(name, new Rotor(rotor, (float)Math.Abs(desiredVelocity), rotor.LowerLimitRad, rotor.UpperLimitRad));
            //}

            Runtime.UpdateFrequency = UpdateFrequency.None;
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
            if ((updateSource & (UpdateType.Trigger | UpdateType.Terminal | UpdateType.Script)) != 0)
            {
                RunCommand(argument);
            }

            if ((updateSource & UpdateType.Update10) != 0)
            {
                PeriodicCleanupCheck();
            }
        }

        void RunCommand(string argument)
        {
            #region Trust Issues

            if (!_commandLine.TryParse(argument))
            {
                Stop();
                return;
            }

            float setAngle;
            Rotor selectedRotor;
            switch (_commandLine.ArgumentCount)
            {
                case 0:
                    Stop();
                    return;
                case 1:
                    if (!TryParseDesiredAngle(_commandLine.Argument(0), out setAngle)) return;
                    RotateAllRotors(setAngle);
                    break;
                case 2:
                    if (!TryParseDesiredAngle(_commandLine.Argument(0), out setAngle)) return;
                    if (!_rotorDict.TryGetValue(_commandLine.Argument(1), out selectedRotor))
                    {
                        Echo($"Rotor with the name '{_commandLine.Argument(1)}' is invalid or not registered!");
                        return;
                    }
                    RotateSingleRotor(selectedRotor, setAngle);
                    break;
                default:
                    Stop();
                    return;
            }

            #endregion Trust Issues
        }

        void RotateAllRotors(float Angle)
        {
            bool needsCleaup = false;
            foreach (var rotor in _rotorDict)
            {
                if (rotor.Value.RotateToDeg(Angle))
                {
                    _cleanupList.AddLast(rotor.Value);
                    needsCleaup |= true;
                }
            }
            if (needsCleaup) Runtime.UpdateFrequency |= UpdateFrequency.Update10;
        }

        void RotateSingleRotor(Rotor rotor, float Angle)
        {
            if (rotor.RotateToDeg(Angle))
            {
                _cleanupList.AddLast(rotor);
                Runtime.UpdateFrequency |= UpdateFrequency.Update10;
            }
        }

        void PeriodicCleanupCheck()
        {
            if (_cleanupList.Count == 0)
            {
                // Remove Update10 Frequency if list is empty
                Runtime.UpdateFrequency &= ~UpdateFrequency.Update10;
                return;
            }
            LinkedListNode<Rotor> Node = _cleanupList.First;
            do
            {
                var rotor = Node.Value;
                if (rotor.HasReachedTargetRotation())
                {
                    rotor.ResetToDefault();
                    var rmNode = Node;
                    Node = Node.Next;
                    _cleanupList.Remove(rmNode);
                }
                else
                {
                    Node = Node.Next;
                }
            } while (Node != null);
        }

        void Stop()
        {
            //Loop through each rotor on the list
            foreach (var rotor in _rotorDict)
            {
                rotor.Value.ResetToDefault();
                //Lock
                //if (lockRotors)
                //{
                //    rotor.Value.LockRotor();
                //}
            }
        }

        float CalcAngleDif(float from, float to)
        {
            // Use a modulo to handle angles that go around the circle multiple times.
            float result = to % 360.0f - from % 360.0f;

            if (result > 180.0f)
            {
                result -= 360.0f;
            }

            if (result <= -180.0f)
            {
                result += 360.0f;
            }
            return result;
        }
        double CalcAngleDif(double from, double to)
        {
            // Use a modulo to handle angles that go around the circle multiple times.
            double result = to % 360.0 - from % 360.0;

            if (result > 180.0)
            {
                result -= 360.0;
            }

            if (result <= -180.0)
            {
                result += 360.0;
            }
            return result;
        }

        // compare if values are within Similar to a specified delta
        public static bool AreSimilar(float A, float B, float delta = 0.0001f)
        {
            return (Math.Abs(A - B) < delta);
        }
        public static bool AreSimilar(double A, double B, double delta = 0.0001)
        {
            return (Math.Abs(A - B) < delta);
        }
        public static bool AreSimilar(int A, int B, int delta = 0)
        {
            return (Math.Abs(A - B) < delta);
        }

        // Returns "value" limited to the range of "min" to "max".
        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        private bool TryParseDesiredAngle(string s, out float Angle)
        {
            if (!float.TryParse(s, out Angle))
            {
                Echo($"Invalid Command for Angle, expected Number - Found: {_commandLine.Argument(0)}");
                return false;
            }
            Angle %= 360f;
            return true;
        }

        public static float RadiansToDegrees(float angle) => angle * (180.0f / (float)Math.PI);

        public static float DegreesToRadians(float angle) => angle * ((float)Math.PI / 180.0f);
    }
}
