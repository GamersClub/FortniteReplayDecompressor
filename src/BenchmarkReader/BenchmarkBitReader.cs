﻿using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Text;
using Unreal.Core;

namespace BenchmarkReader
{
    [MemoryDiagnoser]
    public class BenchmarkBitReader
    {
        private readonly BitReader Reader;

        public BenchmarkBitReader()
        {
            Random rnd = new Random();
            Byte[] b = new Byte[10];
            rnd.NextBytes(b);
            Reader = new BitReader(b);
        }

        [Benchmark]
        public void ReadBit()
        {
            Reader.ReadBit();
        }

        [Benchmark]
        public void ReadBitsToInt()
        {
            Reader.ReadBitsToInt(7);
        }

        [Benchmark]
        public void ReadByte()
        {
            Reader.ReadByte();
        }

        [Benchmark]
        public void ReadBytes()
        {
            Reader.ReadBytes(5);
        }

        [Benchmark]
        public void BitReader()
        {
            Random rnd = new Random();
            Byte[] b = new Byte[10];
            rnd.NextBytes(b);
            var reader = new BitReader(b);
        }
    }
}
