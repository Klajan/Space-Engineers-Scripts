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
using System.Text.RegularExpressions;
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
        public class MyConfiguration
        {
            private List<string> _rotorNames = new List<string>();
            private float _defaultVelocity;
            private bool _isInitalized = false;
            private string _errorString;

            public List<string> RotorNames
            {
                get
                {
                    return _rotorNames;
                }
            }

            public float DefaultVelocity
            {
                get
                {
                    return _defaultVelocity;
                }
            }

            public bool IsInitalized
            {
                get
                {
                    return _isInitalized;
                }
            }

            public string ErrorString
            {
                get
                {
                    return _errorString;
                }
            }

            public MyConfiguration()
            {

                _defaultVelocity = 1.5f;
            }

            public bool ReadConfig(string customData)
            {
                if (customData == null || customData.Length == 0)
                {
                    _errorString = "Empty Custom Data";
                    return false;
                }

                //Try to parse DefaultVelocity
                var velocityMatch = System.Text.RegularExpressions.Regex.Match(customData, @"DesiredVelocity=(.+)[\r\n]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!velocityMatch.Success)
                {
                    _errorString = "Could not find DesiredVelocity";
                    return false;
                }
                if (!float.TryParse(velocityMatch.Groups[1].Value.Trim(), out _defaultVelocity))
                {
                    _errorString = $"Could not parse DesiredVelocity: '{velocityMatch.Groups[1].Value.Trim()}'";
                    return false;
                }

                //Try to get Rotor Names
                var namesMatch = System.Text.RegularExpressions.Regex.Match(customData, @"RotorNames=[\s\S]*?\[([\s\S]*)\][\r\n]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!namesMatch.Success)
                {
                    _errorString = "Could not find RotorNames section";
                    return false;
                }

                var nameMatchess = System.Text.RegularExpressions.Regex.Matches(namesMatch.Groups[1].Value, @"""([^""\r\n]+)"",?", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2));
                if (nameMatchess.Count == 0)
                {
                    _errorString = "Empty RotorNames array";
                    return false;
                }
                foreach (System.Text.RegularExpressions.Match nameMatch in nameMatchess)
                {
                    if (!nameMatch.Success) continue;
                    _errorString = "Could not parse RotorNames";
                    _rotorNames.Add(nameMatch.Groups[1].Value);
                }

                _isInitalized = true;
                return true;
            }
        }
    }
}
