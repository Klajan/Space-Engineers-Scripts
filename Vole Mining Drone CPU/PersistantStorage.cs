using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
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
            string Serialize();
            // Serialize float with .ToString("G9") and double with .ToString("G17")
            bool Deserialize(string dataString);
        }

        public class PersistantStorage
        {

            private readonly Program _program;
            private readonly List<ISaveable> storables = new List<ISaveable>();
            const char seperator = '\u0092';
            const char itemSeperator = '\n';

            public PersistantStorage(Program program)
            {
                _program = program;
            }

            public void Register(ISaveable saveable)
            {
                storables.Add(saveable);
            }

            public string SerializeAll()
            {
                List<string> data = new List<string>(storables.Count);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < storables.Count; i++)
                {
                    sb.Clear();
                    if (!storables[i].ShouldSerialize()) continue;
                    // base64 Encode String to mask potential conflicting sperators
                    // var base64Str = Convert.ToBase64String(Encoding.Unicode.GetBytes(storables[i].Serialize()));
                    sb.Append(i);
                    sb.Append(seperator);
                    sb.Append(Convert.ToBase64String(Encoding.Unicode.GetBytes(storables[i].Serialize())));
                    // Calculate CRC16
                    var bytes = Encoding.Unicode.GetBytes(sb.ToString());
                    ushort crc = calcCRC16(bytes);
                    sb.Append(seperator);
                    sb.Append(crc);
                    //serialString += seperator + crc.ToString("x4");
                    var str = sb.ToString();
                    _program.Echo(str);
                    data.Add(str);
                }
                return string.Join(itemSeperator.ToString(), data);
            }

            public bool DeserializeAll(string storageString)
            {
                bool success = true;
                var items = storageString.Split(itemSeperator);
                foreach (var item in items)
                {
                    var split = item.Split(seperator);
                    if (split.Length != 3) { _program.Echo("Can't split Message"); return false; } // Could not split message
                    // Check CRC16
                    ushort crc = calcCRC16(Encoding.Unicode.GetBytes(split[0] + seperator + split[1]));
                    ushort strCrc = ushort.Parse(split[2]);
                    if (strCrc != crc) { _program.Echo("Invalid Checksum"); return false; }
                    // Get Index for registred ISavable
                    int index = int.Parse(split[0]);
                    if (index >= storables.Count) return false;
                    // Decode base64 String and Deserialize
                    var baseStr = Encoding.Unicode.GetString(Convert.FromBase64String(split[1]));
                    success &= storables[index].Deserialize(baseStr);
                }
                return success;
            }

            public static ushort calcCRC16(byte[] data, int offset, int length)
            {
                const ushort h1 = 0x8000;
                const ushort h2 = 0x1021;
                ushort wData, wCRC = 0;
                int end = offset + length;
                if (!(offset + end <= data.Length)) throw new InvalidOperationException();
                for (int i = offset; i < end; i++)
                {
                    wData = Convert.ToUInt16(data[i] << 8);
                    for (int j = 0; j < 8; j++, wData <<= 1)
                    {
                        ushort a = (ushort)((wCRC ^ wData) & h1);
                        if (a != 0)
                        {
                            wCRC = (ushort)((wCRC << (ushort)1u) ^ h2);
                        }
                        else
                        {
                            wCRC <<= 1;
                        }
                    }
                }
                return wCRC;
            }

            public static ushort calcCRC16(byte[] data)
            {
                return calcCRC16(data, 0, data.Length);
            }
        }
    }
}
