using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BunkerTidyUp.Trainer.Shared
{
    [DataContract]
    public sealed class ModSettings
    {
        [DataMember(Order = 1)] public int Schema { get; set; } = 1;
        [DataMember(Order = 2)] public long Revision { get; set; }
        [DataMember(Order = 3)] public bool ExpandCarry { get; set; }
        [DataMember(Order = 4)] public bool ExpandAutoPickup { get; set; }
        [DataMember(Order = 5)] public bool ExpandAutoPlace { get; set; }
        [DataMember(Order = 6)] public bool ExpandContinuousPlace { get; set; }
        [DataMember(Order = 7)] public bool MoreStars { get; set; }
        [DataMember(Order = 8)] public bool InfiniteStars { get; set; }
        [DataMember(Order = 9)] public bool NoCooldown { get; set; }
        [DataMember(Order = 10)] public bool MaxAll { get; set; }
        [DataMember(Order = 11)] public bool RestoreRequested { get; set; }
        [DataMember(Order = 12)] public bool HoldToDrop { get; set; }

        public bool IsExpansionEnabled(string category)
        {
            switch (category)
            {
                case "carry": return ExpandCarry;
                case "autoPickup": return ExpandAutoPickup;
                case "autoPlace": return ExpandAutoPlace;
                case "continuousPlace": return ExpandContinuousPlace;
                default: return false;
            }
        }
    }

    [DataContract]
    public sealed class PluginStatus
    {
        [DataMember(Order = 1)] public int Schema { get; set; } = 1;
        [DataMember(Order = 2)] public string State { get; set; } = "waiting";
        [DataMember(Order = 3)] public string Message { get; set; } = "等待游戏插件启动";
        [DataMember(Order = 4)] public string GameVersion { get; set; } = "";
        [DataMember(Order = 5)] public string PluginVersion { get; set; } = "";
        [DataMember(Order = 6)] public int Slot { get; set; } = -1;
        [DataMember(Order = 7)] public long SeenRevision { get; set; }
        [DataMember(Order = 8)] public long ActiveRevision { get; set; }
        [DataMember(Order = 9)] public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
        [DataMember(Order = 10)] public bool RestoreSaved { get; set; }
        [DataMember(Order = 11)] public List<string> ActiveFeatures { get; set; } = new List<string>();
        [DataMember(Order = 12)] public long RestoreRevision { get; set; }
        [DataMember(Order = 13)] public int RestoreSlot { get; set; } = -1;
        [DataMember(Order = 14)] public int HandsCount { get; set; } = -1;
        [DataMember(Order = 15)] public int HandsCapacity { get; set; } = -1;
    }

    [DataContract]
    public sealed class SlotProgress
    {
        [DataMember(Order = 1)] public int Schema { get; set; } = 1;
        [DataMember(Order = 2)] public int Slot { get; set; }
        [DataMember(Order = 3)] public string SaveFileName { get; set; } = "";
        [DataMember(Order = 4)] public string SaveFingerprint { get; set; } = "";
        [DataMember(Order = 5)] public DateTime SavedUtc { get; set; }
        [DataMember(Order = 6)] public List<LevelEntry> Abilities { get; set; } = new List<LevelEntry>();
        [DataMember(Order = 7)] public List<LevelEntry> Upgrades { get; set; } = new List<LevelEntry>();
        [DataMember(Order = 8)] public bool HandInventoryMetadataPresent { get; set; }
        [DataMember(Order = 9)] public int HandsCount { get; set; } = -1;
        [DataMember(Order = 10)] public int VanillaHandsCapacity { get; set; } = -1;
    }

    [DataContract]
    public sealed class LevelEntry
    {
        [DataMember(Order = 1)] public string Key { get; set; } = "";
        [DataMember(Order = 2)] public int Level { get; set; }
    }

    public static class JsonFile
    {
        public static T? Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                return serializer.ReadObject(stream) as T;
            }
        }

        public static void WriteAtomic<T>(string path, T value)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("配置路径无效");
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var serializer = new DataContractJsonSerializer(typeof(T));
                    serializer.WriteObject(stream, value);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    var backup = path + ".previous";
                    File.Replace(temporary, path, backup, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            catch
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                throw;
            }
        }
    }
}
