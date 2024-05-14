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
    partial class Program
    {
        public class SimpleMovingAverage
        {
            private readonly int _k;
            private readonly double[] _values;

            private int _index = 0;
            private double _sum;

            public SimpleMovingAverage(int k)
            {
                if (k <= 0) throw new ArgumentException(nameof(k), "Must be greater than 0");

                _k = k;
                _values = new double[k];
            }

            public double Update(double nextInput)
            {
                // calculate the new sum
                _sum = _sum - _values[_index] + nextInput;

                // overwrite the old value with the new one
                _values[_index] = nextInput;

                // increment the index (wrapping back to 0)
                _index = (_index + 1) % _k;

                // calculate the average
                return ((double)_sum) / _k;
            }
        }
    }
}
