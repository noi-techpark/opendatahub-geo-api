// SPDX-FileCopyrightText: NOI Techpark <digital@noi.bz.it>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace VectorTilesApi.Tests;

/// <summary>
/// Minimal Mapbox Vector Tile (protobuf) reader, decodes only the feature properties.
/// See https://github.com/mapbox/vector-tile-spec/blob/master/2.1/vector_tile.proto
/// </summary>
public static class MvtReader
{
    public static List<Dictionary<string, object?>> ReadFeatures(byte[] tile)
    {
        var features = new List<Dictionary<string, object?>>();
        var reader = new ProtoReader(tile);

        while (reader.Next(out var field, out var wireType))
        {
            // Tile.layers = 3
            if (field == 3)
                features.AddRange(ReadLayer(reader.ReadBytes()));
            else
                reader.Skip(wireType);
        }

        return features;
    }

    private static List<Dictionary<string, object?>> ReadLayer(byte[] layer)
    {
        var keys = new List<string>();
        var values = new List<object?>();
        var rawFeatures = new List<List<uint>>();
        var reader = new ProtoReader(layer);

        while (reader.Next(out var field, out var wireType))
        {
            switch (field)
            {
                case 2: rawFeatures.Add(ReadFeatureTags(reader.ReadBytes())); break;
                case 3: keys.Add(reader.ReadString()); break;
                case 4: values.Add(ReadValue(reader.ReadBytes())); break;
                default: reader.Skip(wireType); break;
            }
        }

        return rawFeatures.Select(tags =>
        {
            var properties = new Dictionary<string, object?>();
            for (int i = 0; i + 1 < tags.Count; i += 2)
                properties[keys[(int)tags[i]]] = values[(int)tags[i + 1]];
            return properties;
        }).ToList();
    }

    private static List<uint> ReadFeatureTags(byte[] feature)
    {
        var tags = new List<uint>();
        var reader = new ProtoReader(feature);

        while (reader.Next(out var field, out var wireType))
        {
            // Feature.tags = 2 (packed uint32)
            if (field == 2)
            {
                var packed = new ProtoReader(reader.ReadBytes());
                while (!packed.End)
                    tags.Add((uint)packed.ReadVarint());
            }
            else
                reader.Skip(wireType);
        }

        return tags;
    }

    private static object? ReadValue(byte[] value)
    {
        var reader = new ProtoReader(value);
        object? result = null;

        while (reader.Next(out var field, out var wireType))
        {
            result = field switch
            {
                1 => reader.ReadString(),
                2 => BitConverter.ToSingle(reader.ReadFixed(4)),
                3 => BitConverter.ToDouble(reader.ReadFixed(8)),
                4 => (long)reader.ReadVarint(),
                5 => (long)reader.ReadVarint(),
                6 => DecodeZigZag(reader.ReadVarint()),
                7 => reader.ReadVarint() != 0,
                _ => SkipAndReturnNull(reader, wireType)
            };
        }

        return result;
    }

    private static object? SkipAndReturnNull(ProtoReader reader, int wireType)
    {
        reader.Skip(wireType);
        return null;
    }

    private static long DecodeZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    private sealed class ProtoReader(byte[] buffer)
    {
        private int _position;

        public bool End => _position >= buffer.Length;

        public bool Next(out int field, out int wireType)
        {
            field = 0;
            wireType = 0;
            if (End)
                return false;

            var key = ReadVarint();
            field = (int)(key >> 3);
            wireType = (int)(key & 7);
            return true;
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            byte b;
            do
            {
                b = buffer[_position++];
                result |= (ulong)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return result;
        }

        public byte[] ReadBytes()
        {
            var length = (int)ReadVarint();
            var bytes = buffer.AsSpan(_position, length).ToArray();
            _position += length;
            return bytes;
        }

        public string ReadString() => System.Text.Encoding.UTF8.GetString(ReadBytes());

        public byte[] ReadFixed(int length)
        {
            var bytes = buffer.AsSpan(_position, length).ToArray();
            _position += length;
            return bytes;
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(); break;
                case 1: _position += 8; break;
                case 2: ReadBytes(); break;
                case 5: _position += 4; break;
                default: throw new InvalidDataException($"Unsupported wire type {wireType}");
            }
        }
    }
}
