using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        public interface ISaveable
        {
            bool ShouldSerialize();
            byte[] Serialize();
            bool Deserialize(byte[] data);
            ushort Salt { get; }
        }

        public class PersistantStorage
        {

            private readonly Program _program;
            private readonly List<ISaveable> ISaveableList = new List<ISaveable>();
            const char seperator = '\u0092';
            const char itemSeperator = '\n';

            public PersistantStorage(Program program)
            {
                _program = program;
            }

            public void Register(ISaveable saveable)
            {
                ISaveableList.Add(saveable);
            }

            public string SerializeAll(bool forceSave = false)
            {
                List<string> data = new List<string>(ISaveableList.Count);
                for (ushort i = 0; i < ISaveableList.Count; i++)
                {
                    if (!forceSave && !ISaveableList[i].ShouldSerialize()) continue;
                    // base64 Encode bytes
                    var serialData = ISaveableList[i].Serialize();
                    // Calculate CRC16 & XOR with index
                    ushort crc = (ushort)(CalcCRC16(serialData) ^ i ^ ISaveableList[i].Salt);
                    string output = i.ToString() + seperator + Convert.ToBase64String(serialData) + seperator + crc.ToString();
                    data.Add(output);
                }
                if (data.Count == 0) return string.Empty;
                return string.Join(itemSeperator.ToString(), data);
            }

            public bool DeserializeAll(string storageString)
            {
                bool success = true;
                if (storageString == null) return true;
                var items = storageString.Split(itemSeperator);
                foreach (var item in items)
                {
                    var split = item.Split(seperator);
                    if (split.Length != 3) { _program.Echo("Can't split Message"); return false; } // Could not split message
                    // Check CRC16
                    ushort index;
                    if(!ushort.TryParse(split[0], out index) && index >= ISaveableList.Count) { _program.Echo("Invalid Message (index invalid our out of bounds)"); return false; }
                    byte[] serialData = Convert.FromBase64String(split[1]);

                    ushort crc = (ushort)(CalcCRC16(serialData) ^ index ^ ISaveableList[index].Salt);
                    ushort strCrc = ushort.Parse(split[2]);
                    if (strCrc != crc) { _program.Echo("Invalid Checksum"); return false; }
                    // Decode base64 String and Deserialize
                    success &= ISaveableList[index].Deserialize(serialData);
                }
                return success;
            }

            public static ushort CalcCRC16(byte[] data, int offset, int length)
            {
                ushort wData, wCRC = 0;
                int end = offset + length;
                if (!(offset + end <= data.Length)) throw new InvalidOperationException();
                for (int i = offset; i < end; i++)
                {
                    wData = Convert.ToUInt16(data[i] << 8);
                    for (int j = 0; j < 8; j++, wData <<= 1)
                    {
                        ushort a = (ushort)((wCRC ^ wData) & 0x8000);
                        if (a != 0)
                        {
                            wCRC = (ushort)((wCRC << (ushort)1u) ^ 0x1021);
                        }
                        else
                        {
                            wCRC <<= 1;
                        }
                    }
                }
                return wCRC;
            }

            public static ushort CalcCRC16(byte[] data)
            {
                return CalcCRC16(data, 0, data.Length);
            }
        }
    }
}
