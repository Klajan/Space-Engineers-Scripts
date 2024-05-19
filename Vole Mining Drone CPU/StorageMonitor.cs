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
        public class StorageMonitor //: ISaveable
        {
            private List<IMyInventory> inventories = new List<IMyInventory>();
            private HashSet<MyItemType> itemBlackList = new HashSet<MyItemType>();
            private float blacklistAllowedPercent;
            private const float cargoFilledPercent = 0.85f;

            public MyFixedPoint TotalVolume { get; private set; } = new MyFixedPoint();
            public MyFixedPoint CurrentVolume { get; private set; } = new MyFixedPoint();
            public MyFixedPoint BlacklistVolume { get; private set; } = new MyFixedPoint();

            private const float oreVolumePerKilo = 0.00037f; // kg/m³
            public static readonly MyItemType _stone = MyItemType.MakeOre("Stone");
            public static readonly MyItemType _ice = MyItemType.MakeOre("Ice");

            #region SaveOnExitVariables
            #endregion

            public StorageMonitor(IEnumerable<IMyCargoContainer> cargoContainers, float blacklistAllowedPercent = 0.10f, IEnumerable<MyItemType> itemBlackList = null)
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
                    TotalVolume += cargo.GetInventory().MaxVolume;
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
                    TotalVolume += item.MaxVolume;
                }
            }

            public void RegisterOnBlacklist(IEnumerable<MyItemType> items)
            {
                foreach (var item in items)
                {
                    this.itemBlackList.Add(item);
                }
            }

            public float GetBlacklistVolumeFillFactor()
            {
                return (float)BlacklistVolume / (float)TotalVolume;
            }

            public float GetBlacklistCargoFraction()
            {
                return CurrentVolume != 0 ? (float)BlacklistVolume / (float)CurrentVolume : 0;
            }

            public bool CheckInventoryFull()
            {
                CurrentVolume = 0;
                BlacklistVolume = 0;
                foreach (var item in inventories)
                {
                    CurrentVolume += item.CurrentVolume;
                    foreach (var ban in itemBlackList)
                    {
                        BlacklistVolume += MyFixedPoint.MultiplySafe(item.GetItemAmount(ban), oreVolumePerKilo);
                    }
                }
                if(((float)CurrentVolume / (float)TotalVolume) >= cargoFilledPercent)
                {
                    return ((float)BlacklistVolume / (float)CurrentVolume) <= blacklistAllowedPercent;
                }
                return false;
            }

        }
    }
}
