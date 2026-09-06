// Hand-written stand-ins for the handful of game / BepInEx types the tested source
// mentions. Deliberately minimal: nothing here needs to behave like Valheim beyond what
// the tests assert on — it only needs to compile and let the real logic run.
//
// Merged from RavenEye (ZPackage serialization, ZNet.PlayerInfo, RosterEntry types) and
// RagnaroksWrath (ZoneSystem maths, World, ZDO, HarmonyLib.AccessTools, TestLog). The
// stubs compiled once and used across both reference projects.
//
// The one stub that DOES mirror the game is ZPackage: it wraps a BinaryWriter/BinaryReader
// over a MemoryStream with the same primitive encodings the real one uses (decompiled
// 0.221.12: Write(string) → BinaryWriter.Write(string); Write(ZDOID) → long UserID then
// uint ID; Write(Vector3) → three floats), so the packet round-trip test exercises the
// SHIPPING writer against the SHIPPING reader over real bytes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// ---- UnityEngine ------------------------------------------------------------------

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public override string ToString() => $"({x},{y},{z})";
    }

    public struct Vector2i
    {
        public int x, y;
        public Vector2i(int x, int y) { this.x = x; this.y = y; }
        public override string ToString() => $"({x},{y})";
    }
}

// ---- Valheim ----------------------------------------------------------------------
// Global-namespace types in the real game, so the stubs are too.

public struct ZDOID : IEquatable<ZDOID>
{
    public static readonly ZDOID None = new ZDOID(0L, 0u);

    public long UserID { get; }
    public uint ID { get; }

    public ZDOID(long userID, uint id) { UserID = userID; ID = id; }

    public bool IsNone() => UserID == 0L && ID == 0u;

    public bool Equals(ZDOID other) => UserID == other.UserID && ID == other.ID;
    public override bool Equals(object obj) => obj is ZDOID other && Equals(other);
    public override int GetHashCode() => UserID.GetHashCode() ^ (int)ID;
    public static bool operator ==(ZDOID a, ZDOID b) => a.Equals(b);
    public static bool operator !=(ZDOID a, ZDOID b) => !a.Equals(b);
    public override string ToString() => $"{UserID}:{ID}";
}

public class ZPackage
{
    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;
    private readonly BinaryReader _reader;

    public ZPackage()
    {
        _stream = new MemoryStream();
        _writer = new BinaryWriter(_stream, Encoding.UTF8);
        _reader = new BinaryReader(_stream, Encoding.UTF8);
    }

    public ZPackage(byte[] data) : this()
    {
        _stream.Write(data, 0, data.Length);
        _stream.Position = 0;
    }

    public byte[] GetArray() { _writer.Flush(); return _stream.ToArray(); }
    public long Size => _stream.Length;

    public void Write(int v)    => _writer.Write(v);
    public void Write(bool v)   => _writer.Write(v);
    public void Write(float v)  => _writer.Write(v);
    public void Write(string v) => _writer.Write(v);
    public void Write(ZDOID id) { _writer.Write(id.UserID); _writer.Write(id.ID); }
    public void Write(UnityEngine.Vector3 v) { _writer.Write(v.x); _writer.Write(v.y); _writer.Write(v.z); }

    public int ReadInt()       => _reader.ReadInt32();
    public bool ReadBool()     => _reader.ReadBoolean();
    public float ReadSingle()  => _reader.ReadSingle();
    public string ReadString() => _reader.ReadString();
    public ZDOID ReadZDOID()   => new ZDOID(_reader.ReadInt64(), _reader.ReadUInt32());
    public UnityEngine.Vector3 ReadVector3()
        => new UnityEngine.Vector3(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
}

public static class ZoneSystem
{
    public const float ZoneSize = 64f;

    public static UnityEngine.Vector2i GetZone(UnityEngine.Vector3 point)
        => new UnityEngine.Vector2i(
            (int)Math.Floor((point.x + ZoneSize / 2f) / ZoneSize),
            (int)Math.Floor((point.z + ZoneSize / 2f) / ZoneSize));

    public static UnityEngine.Vector3 GetZonePos(UnityEngine.Vector2i id)
        => new UnityEngine.Vector3(id.x * ZoneSize, 0f, id.y * ZoneSize);
}

public static class FileHelpers
{
    public enum FileSource { Auto = 0, Local = 1, Cloud = 2, Legacy = 3 }
}

public class World
{
    public long m_uid;
    public static string GetWorldSavePath(FileHelpers.FileSource fileSource) => System.IO.Path.GetTempPath();
}

public class ZNet
{
    public static ZNet instance => null;
    public static World GetWorldIfIsHost() => null;
    public List<ZDO> GetAllCharacterZDOS() => new List<ZDO>();

    public struct PlayerInfo
    {
        public string m_name;
        public ZDOID m_characterID;
        public bool m_publicPosition;
        public UnityEngine.Vector3 m_position;
    }
}

public class ZDO
{
    public UnityEngine.Vector3 Position;

    public bool IsValid() => true;
    public UnityEngine.Vector3 GetPosition() => Position;
}

// ---- HarmonyLib -------------------------------------------------------------------

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static TField FieldRefAccess<TObject, TField>(TObject instance, string fieldName)
            => default;
    }
}

// ---- the plugin's logger (for RagnaroksWrath tests) ---------------------------

namespace RavenIron.RagnaroksWrath
{
    public class TestLog
    {
        public void LogInfo(object o)    => Console.WriteLine($"      [info]  {o}");
        public void LogWarning(object o) => Console.WriteLine($"      [warn]  {o}");
        public void LogError(object o)   => Console.WriteLine($"      [error] {o}");
    }

    public static class RagnaroksWrath
    {
        public static readonly TestLog Log = new TestLog();
    }
}

// ---- BepInEx.Configuration --------------------------------------------------------

namespace BepInEx.Configuration
{
    public class AcceptableValueRange<T>
    {
        public T Min, Max;
        public AcceptableValueRange(T min, T max) { Min = min; Max = max; }
    }

    public class ConfigDescription
    {
        public string Description;
        public object AcceptableValues;
        public ConfigDescription(string description, object acceptableValues = null)
        {
            Description = description;
            AcceptableValues = acceptableValues;
        }
    }

    public class ConfigEntry<T>
    {
        public T Value { get; set; }
        public ConfigDescription Description { get; }
        public ConfigEntry(T value, ConfigDescription description) { Value = value; Description = description; }
    }

    public class ConfigFile
    {
        public readonly List<string> Keys = new List<string>();

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = null)
        {
            Keys.Add(section + "." + key);
            return new ConfigEntry<T>(defaultValue, new ConfigDescription(description));
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description)
        {
            Keys.Add(section + "." + key);
            return new ConfigEntry<T>(defaultValue, description);
        }
    }
}
