// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements.Collections;

namespace EchoWeaverAP.Randomizer;

class GameData(string roomSeed, int slot, Dictionary<string, object> slotData, Dictionary<long, (long ItemId, ItemImportance Importance, string Description)> scoutedLocations)
{
    public string RoomSeed = roomSeed;
    public int Slot = slot;
    public ReadOnlyDictionary<string, object> SlotData = new(slotData);
    public ReadOnlyDictionary<long, (long ItemId, ItemImportance Importance, string Description)> ScoutedLocations = new(scoutedLocations);
    public HashSet<long> CheckedLocations = [];
    public int LastProcessedItemIndex = -1;
    public bool GoalAchieved = false;

    [JsonIgnore]
    public Goal Goal { get { return GetSetting("goal", Goal.BreakTheCycle); } }

    [JsonIgnore]
    private Dictionary<KeypadInstance, (List<int>[], (Digit, D8Symmetry)[])> _keypads = null;
    [JsonIgnore]
    public Dictionary<KeypadInstance, (List<int>[] Passcodes, (Digit Digit, D8Symmetry Symmetry)[] Buttons)> Keypads
    {
        get
        {
            if (_keypads == null && GetSetting<object>("keypads", null) is JToken t)
            {
                JsonSerializer serializer = new();
                serializer.Converters.Add(new TupleConverter());
                _keypads = t.ToObject<Dictionary<KeypadInstance, (List<int>[], (Digit, D8Symmetry)[])>>(serializer);
            }
            return _keypads ?? [];
        }
    }

    [JsonIgnore]
    private int[][] _frequencies = null;
    [JsonIgnore]
    public int[][] Frequencies
    {
        get
        {
            if (_frequencies == null && GetSetting<object>("frequencies", null) is JToken t)
            {
                _frequencies = t.ToObject<int[][]>();
            }
            return _frequencies ?? [];
        }
    }

    private T GetSetting<T>(string key, T dflt)
    {
        try
        {
            return (T)SlotData.Get(key);
        }
        catch (Exception)
        {
            return dflt;
        }
    }

    private class TupleConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(ITuple).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JArray array = JArray.Load(reader);
            Type[] genericArgs = objectType.GetGenericArguments();
            if (array.Count != genericArgs.Length)
                throw new JsonSerializationException($"Tuple expects {genericArgs.Length} items, but JSON array contained {array.Count} items");

            object[] values = new object[genericArgs.Length];
            for (int i = 0; i < genericArgs.Length; ++i)
            {
                values[i] = array[i].ToObject(genericArgs[i], serializer);
            }
            return Activator.CreateInstance(objectType, values);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanWrite => false;
    }
}
