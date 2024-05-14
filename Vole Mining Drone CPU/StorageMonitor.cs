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
using IMyCargoContainer = Sandbox.ModAPI.Ingame.IMyCargoContainer;

namespace IngameScript
{
    partial class Program
    {
        public class StorageMonitor : ISaveable
        {
            private List<IMyInventory> inventories = new List<IMyInventory>();
            private HashSet<MyItemType> itemBlackList = new HashSet<MyItemType>();
            private MyFixedPoint totalVolume = new MyFixedPoint();
            private double blacklistAllowedPercent;

            private const float oreVolumePerKilo = 0.00037f; // kg/m³
            public static readonly MyItemType _stone = MyItemType.MakeOre("Stone");
            public static readonly MyItemType _ice = MyItemType.MakeOre("Ice");

            #region SaveOnExitVariables
            #endregion

            public StorageMonitor(IEnumerable<IMyCargoContainer> cargoContainers, double blacklistAllowedPercent = 0.10, IEnumerable<MyItemType> itemBlackList = null)
            {
                this.blacklistAllowedPercent = blacklistAllowedPercent;
                if (itemBlackList == null)
                {
                    this.itemBlackList.Add(_stone);
                    this.itemBlackList.Add(_ice);
                }
                else
                {
                    foreach (var item in itemBlackList)
                    {
                        this.itemBlackList.Add(item);
                    }
                }
                foreach (var cargo in cargoContainers)
                {
                    inventories.Add(cargo.GetInventory());
                    totalVolume += cargo.GetInventory().MaxVolume;
                }
            }

            #region ISaveable
            private const int minDataLength = sizeof(int) * 1 + sizeof(bool) * 1;
            public byte[] Serialize()
            {
                byte[] data = new byte[minDataLength];
                return data;
            }

            public bool Deserialize(byte[] data)
            {
                return true;
            }

            public bool ShouldSerialize()
            {
                return false;
            }
            #endregion

            public void RegisterCargo(IEnumerable<IMyInventory> inventories)
            {
                foreach (var item in inventories)
                {
                    this.inventories.Add(item);
                }
            }

            public void RegisterOnBlacklist(IEnumerable<MyItemType> items)
            {
                foreach (var item in items)
                {
                    this.itemBlackList.Add(item);
                }
            }

            public bool CheckInventoryFull()
            {
                var isFull = true;
                MyFixedPoint currentVolume = new MyFixedPoint();
                MyFixedPoint blacklistAmount = new MyFixedPoint();
                foreach (var item in inventories)
                {
                    isFull &= item.IsFull;
                    currentVolume += item.CurrentVolume;
                    foreach (var ban in itemBlackList)
                    {
                        blacklistAmount += item.GetItemAmount(ban);
                    }
                }
                if(isFull)
                {
                    blacklistAmount = MyFixedPoint.MultiplySafe(blacklistAmount, oreVolumePerKilo);
                    return ((double)blacklistAmount / (double)totalVolume) <= blacklistAllowedPercent;
                }
                return false;
            }

        }
    }
}
