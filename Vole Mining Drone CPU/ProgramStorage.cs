using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
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
        public class ProgramStorage : ISaveable
        {
            Program _program;
            #region SaveOnExitVariables
            public bool IsEnabled { get; set; } = false;
            public int _maxDrillingDepth { get; set; } = 75;
            public bool EjectStone { get; set; } = true;
            private UpdateFrequency _updateFrequency;
            #endregion

            public int MaxDrillingDepth
            {
                get { return _maxDrillingDepth; }
                set { _maxDrillingDepth = Math.Max(value, 0); }
            }

            public UpdateFrequency UpdateFrequency
            {
                get { return _updateFrequency; }
                set
                {
                    _updateFrequency = value;
                    _program.Runtime.UpdateFrequency = value;
                }
            }

            public ProgramStorage(Program program)
            {
                _program = program;
                UpdateFrequency = UpdateFrequency.Update100;
            }

            #region ISaveable
            public ushort Salt { get { return 0x0002; } }
            private const int minDataLength = sizeof(int) * 1 + sizeof(bool) * 2 + sizeof(byte);
            public byte[] Serialize()
            {
                byte[] data = BitConverter.GetBytes(IsEnabled)
                    .Concat(BitConverter.GetBytes(_maxDrillingDepth))
                    .Concat(BitConverter.GetBytes(EjectStone))
                    .Concat(new byte[] { (byte)_updateFrequency })
                    .ToArray();
                return data;
            }

            public bool Deserialize(byte[] data)
            {
                if (data.Length < minDataLength) return false;
                int index = 0;
                IsEnabled = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                _maxDrillingDepth = BitConverter.ToInt32(data, index);
                index += sizeof(int);
                EjectStone = BitConverter.ToBoolean(data, index);
                index += sizeof(bool);
                UpdateFrequency = (UpdateFrequency)data[index];
                index += sizeof(byte);
                return true;
            }

            public bool ShouldSerialize()
            {
                return true;
            }
            #endregion
        }
    }
}
