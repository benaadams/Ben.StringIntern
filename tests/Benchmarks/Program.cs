using System;
using System.Buffers.Text;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

using Ben.Collections.Specialized;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Running;

using Microsoft.Toolkit.HighPerformance.Buffers;
using Microsoft.Toolkit.HighPerformance.Enumerables;
using Microsoft.Toolkit.HighPerformance.Extensions;

using static Ben.Collections.Specialized.StringCache;

// From Sergio0694 benchmark
// https://gist.github.com/Sergio0694/c51cb027e6815d7b592484eebe9e3685
// Needs data 24MB file: https://github.com/dotnet/machinelearning/blob/master/test/data/taxi-fare-train.csv 
// Saved with UTF8 encoding as taxi-fare-train-utf8.csv

namespace StringPoolCsvParsingBenchmark
{
    class Program
    {
        static void Main()
        {
            BenchmarkRunner.Run<ParsingBenchmark>();
        }
    }

    [MemoryDiagnoser]
    [SimpleJob(RunStrategy.Monitoring)]
    public class ParsingBenchmark
    {
        private MemoryOwner<byte> sourceMemory;
        private MemoryOwner<Data> dataMemory;
        private readonly StringPool stringPool1 = new StringPool();
        private readonly StringPool stringPool2 = new StringPool();
        private readonly InternPool internPool = new InternPool();

        [Params("Taxi-data", "2M-Unique", "2M (20k-Values)")]
        public string Dataset { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            // Source: https://github.com/dotnet/machinelearning/blob/master/test/data/taxi-fare-train.csv 
            // Saved with UTF8 encoding
            using Stream stream = Dataset switch
            {
                "Taxi-data" => File.OpenRead("taxi-fare-train-utf8.csv"),
                "2M-Unique" => File.OpenRead("2-million-unique.csv"),
                "2M (20k-Values)" => File.OpenRead("2-million-20k-values.csv"),
                _ => throw new InvalidDataException()
            };

            this.sourceMemory = MemoryOwner<byte>.Allocate((int)stream.Length);

            stream.Read(this.sourceMemory.Span);

            this.dataMemory = MemoryOwner<Data>.Allocate(this.sourceMemory.Span.Count((byte)'\n'), AllocationMode.Clear);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            this.sourceMemory.Dispose();
            this.dataMemory.Span.Clear();
            this.dataMemory.Dispose();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            this.stringPool1.Reset();
            this.stringPool2.Reset();
            this.internPool.Clear();
        }

        [Benchmark(Baseline = true)]
        public void Default()
        {
            var parser = new DefaultParser();

            Parse(ref parser);
        }

        [Benchmark]
        public void MTHP_StringPool_Stackalloc()
        {
            var parser = new StringPoolCustomParser(this.stringPool1);

            Parse(ref parser);
        }

        [Benchmark]
        public void MTHP_StringPool_Encoder()
        {
            var parser = new StringPoolEmbeddedParser(this.stringPool2);

            Parse(ref parser);
        }

        [Benchmark]
        public void StringIntern_Shared()
        {
            var parser = new InternSharedParser();

            Parse(ref parser);
        }

        [Benchmark]
        public void StringIntern_Instance()
        {
            var parser = new InternParser(this.internPool);

            Parse(ref parser);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Parse<T>(ref T parser)
            where T : struct, IStringParser
        {
            var header = true;
            var i = 0;
            var dataSpan = this.dataMemory.Span;

            foreach (var line in new ReadOnlySpanTokenizer<byte>(this.sourceMemory.Span, (byte)'\n'))
            {
                if (header)
                {
                    header = false;
                }
                else
                {
                    ref var data = ref dataSpan[i++];
                    var index = 0;

                    foreach (var item in new ReadOnlySpanTokenizer<byte>(line, (byte)','))
                    {
                        switch (index++)
                        {
                            case 0:
                                data.VendorId = parser.ParseString(item);
                                break;
                            case 1:
                                if (Utf8Parser.TryParse(item, out byte rateCode, out _))
                                {
                                    data.RateCode = rateCode;
                                }

                                break;
                            case 2:
                                if (Utf8Parser.TryParse(item, out byte passengerCount, out _))
                                {
                                    data.PassengerCount = passengerCount;
                                }

                                break;
                            case 3:
                                if (Utf8Parser.TryParse(item, out short tripTimeInSecs, out _))
                                {
                                    data.TripTimeInSecs = tripTimeInSecs;
                                }

                                break;
                            case 4:
                                if (Utf8Parser.TryParse(item, out float tripDistance, out _))
                                {
                                    data.TripDistance = tripDistance;
                                }

                                break;
                            case 5:
                                data.PaymentType = parser.ParseString(item);
                                break;
                            case 6:
                                if (Utf8Parser.TryParse(item, out float fareAmount, out _))
                                {
                                    data.FareAmount = fareAmount;
                                }

                                break;
                        }
                    }
                }
            }
        }
    }

    public interface IStringParser
    {
        string ParseString(ReadOnlySpan<byte> span);
    }

    public readonly struct DefaultParser : IStringParser
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ParseString(ReadOnlySpan<byte> span)
        {
            return Encoding.UTF8.GetString(span);
        }
    }

    public readonly struct InternSharedParser : IStringParser
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ParseString(ReadOnlySpan<byte> span)
        {
            return InternUtf8(span);
        }
    }

    public readonly struct InternParser : IStringParser
    {
        private readonly InternPool pool;

        public InternParser(InternPool pool)
        {
            this.pool = pool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ParseString(ReadOnlySpan<byte> span)
        {
            return this.pool.InternUtf8(span);
        }
    }

    public readonly struct StringPoolCustomParser : IStringParser
    {
        private readonly StringPool pool;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StringPoolCustomParser(StringPool pool)
        {
            this.pool = pool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ParseString(ReadOnlySpan<byte> span)
        {
            Span<char> chars = stackalloc char[span.Length];
            Utf8.ToUtf16(span, chars, out _, out int charsWritten);

            return this.pool.GetOrAdd(chars.Slice(0, charsWritten));
        }
    }

    public readonly struct StringPoolEmbeddedParser : IStringParser
    {
        private readonly StringPool pool;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StringPoolEmbeddedParser(StringPool pool)
        {
            this.pool = pool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ParseString(ReadOnlySpan<byte> span)
        {
            return this.pool.GetOrAdd(span, Encoding.UTF8);
        }
    }

    public struct Data
    {
        public string VendorId;
        public byte RateCode;
        public byte PassengerCount;
        public short TripTimeInSecs;
        public float TripDistance;
        public string PaymentType;
        public float FareAmount;

        public override string ToString()
        {
            return $"{VendorId},{RateCode},{PassengerCount},{TripTimeInSecs},{TripDistance},{PaymentType},{FareAmount}";
        }
    }
}