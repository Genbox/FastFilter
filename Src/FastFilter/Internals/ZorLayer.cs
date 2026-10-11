// Independent implementation of the ZOR peeling/abandonment algorithm description.
// No code is translated from the unlicensed reference repository. See THIRD-PARTY-NOTICES.txt.
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Genbox.FastFilter.Internals;

internal sealed class ZorLayer
{
    private readonly byte[] _data;
    private readonly ulong _seed;
    private readonly int _segmentLength;
    private readonly int _starts;
    private readonly int _width;

    internal ZorLayer(ulong[] signatures, ulong seed, bool abandon, out ulong[] abandoned)
    {
        abandoned = [];
        _width = abandon ? 1 : 2;
        _segmentLength = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(4, Math.Sqrt(signatures.Length)));
        _starts = Math.Max(1, (int)Math.Ceiling((signatures.Length * (abandon ? 1.05 : 1.40)) / _segmentLength) - 3);
        int vertices = checked((_starts + 3) * _segmentLength);
        _data = new byte[signatures.Length == 0 ? 0 : checked(vertices * _width)];
        if (signatures.Length == 0)
            return;

        for (int attempt = 0; attempt < (abandon ? 1 : 128); attempt++)
        {
            _seed = StaticFilterHash.Mix(seed + (ulong)attempt);
            if (Populate(signatures, vertices, abandon, out abandoned))
                return;
        }

        throw new InvalidOperationException("Could not peel the ZOR remainder in 128 attempts.");
    }

    internal uint MemoryUsage => checked((uint)_data.Length + 20);

    internal bool Contains(ulong signature)
    {
        if (_data.Length == 0)
            return false;
        ulong hash = StaticFilterHash.Mix(signature + _seed);
        int fingerprint = Fingerprint(hash);

        int length = _segmentLength;
        int mask = length - 1;
        int first = StaticFilterHash.Reduce(hash, _starts) * length;
        int h0 = first + ((int)hash & mask);
        int h1 = first + length + ((int)BitOperations.RotateLeft(hash, 13) & mask);
        int h2 = first + (2 * length) + ((int)BitOperations.RotateLeft(hash, 26) & mask);
        int h3 = first + (3 * length) + ((int)BitOperations.RotateLeft(hash, 39) & mask);

        if (_width == 1)
            fingerprint ^= _data[h0] ^ _data[h1] ^ _data[h2] ^ _data[h3];
        else
        {
            ReadOnlySpan<ushort> words = MemoryMarshal.Cast<byte, ushort>(_data);
            int value = words[h0] ^ words[h1] ^ words[h2] ^ words[h3];
            fingerprint ^= BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness((ushort)value);
        }

        return fingerprint == 0;
    }

    private int Position(ulong hash, int part)
    {
        int first = StaticFilterHash.Reduce(hash, _starts) * _segmentLength;
        return first + (part * _segmentLength) + ((int)BitOperations.RotateLeft(hash, 13 * part) & (_segmentLength - 1));
    }

    private int Fingerprint(ulong hash) => (int)StaticFilterHash.Mix(hash ^ 0xd6e8feb86659fd93UL) & (_width == 1 ? 255 : 65535);

    private bool Populate(ulong[] signatures, int vertices, bool abandon, out ulong[] abandonedSignatures)
    {
        abandonedSignatures = [];
        int[] degree = new int[vertices];
        int[] incidence = new int[vertices];
        Array.Fill(incidence, -1);
        int[] next = new int[checked(signatures.Length * 4)];
        int[] edges = new int[next.Length];
        bool[] active = new bool[signatures.Length];

        for (int edge = 0; edge < signatures.Length; edge++)
        {
            active[edge] = true;
            ulong hash = StaticFilterHash.Mix(signatures[edge] + _seed);

            for (int part = 0; part < 4; part++)
            {
                int position = Position(hash, part);
                int entry = (edge * 4) + part;
                edges[entry] = position;
                next[entry] = incidence[position];
                incidence[position] = entry;
                degree[position]++;
            }
        }

        Queue<int> queue = new Queue<int>();
        PriorityQueue<int, int> minimum = new PriorityQueue<int, int>();

        for (int vertex = 0; vertex < vertices; vertex++)
        {
            if (degree[vertex] == 1)
                queue.Enqueue(vertex);
            if (abandon && degree[vertex] > 1)
                minimum.Enqueue(vertex, degree[vertex]);
        }

        List<(int Edge, int Vertex)> order = new List<(int, int)>();
        List<ulong> abandoned = new List<ulong>();
        int remaining = signatures.Length;

        while (remaining > 0)
        {
            while (queue.Count > 0)
            {
                int vertex = queue.Dequeue();
                if (degree[vertex] != 1)
                    continue;
                int entry = incidence[vertex];
                while (!active[entry / 4])
                    entry = next[entry];
                int edge = entry / 4;
                order.Add((edge, vertex));
                Remove(edge);
            }

            if (remaining == 0)
                break;
            if (!abandon)
                return false;
            int selected;
            int priority;

            do
            {
                if (!minimum.TryDequeue(out selected, out priority))
                    throw new InvalidOperationException("The remaining graph has no incident vertex.");
            } while (degree[selected] != priority || priority < 2);

            bool kept = false;

            for (int entry = incidence[selected]; entry >= 0; entry = next[entry])
            {
                int edge = entry / 4;
                if (!active[edge])
                    continue;

                if (!kept)
                {
                    kept = true;
                    continue;
                }

                abandoned.Add(signatures[edge]);
                Remove(edge);
            }
        }

        ushort[] solution = new ushort[vertices];

        for (int index = order.Count - 1; index >= 0; index--)
        {
            (int edge, int vertex) = order[index];
            ulong hash = StaticFilterHash.Mix(signatures[edge] + _seed);
            int fingerprint = Fingerprint(hash);
            for (int part = 0; part < 4; part++)
                fingerprint ^= solution[edges[(4 * edge) + part]];
            solution[vertex] = (ushort)fingerprint;
        }

        for (int vertex = 0; vertex < vertices; vertex++)
        {
            _data[vertex * _width] = (byte)solution[vertex];
            if (_width == 2)
                _data[(vertex * _width) + 1] = (byte)(solution[vertex] >> 8);
        }

        abandonedSignatures = abandoned.ToArray();
        return true;

        void Remove(int edge)
        {
            active[edge] = false;
            remaining--;

            for (int part = 0; part < 4; part++)
            {
                int vertex = edges[(4 * edge) + part];
                degree[vertex]--;
                if (degree[vertex] == 1)
                    queue.Enqueue(vertex);
                else if (abandon && degree[vertex] > 1)
                    minimum.Enqueue(vertex, degree[vertex]);
            }
        }
    }
}