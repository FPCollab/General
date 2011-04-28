using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using Packable;
using Packable.StreamBinary;
using Packable.StreamBinary.Generic;
namespace Tester
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            {
                var ms = new MemoryStream();
                var t1 = new Packtest();
                ms.Reset();
                t1.Field9 = null;
                t1.Pack(ms);
                ms.Reset();
                t1.Unpack(ms);
            }
            return;

            var test1 = new SpeedTest(delegate()
            {
                var ms = new MemoryStream();
                var t1 = new Packtest();
                ms.Reset();
                t1.Pack(ms);
                ms.Reset();
                t1.Unpack(ms);
            });

            var test2 = new SpeedTest(delegate()
            {
                var ms = new MemoryStream();
                var t2 = new Packtest2();
                ms.Reset();
                t2.Pack(ms);
                ms.Reset();
                t2.Unpack(ms);
            });


            test2.Test();
            test1.Test();

            Console.WriteLine("My serialize class");
            Console.WriteLine("Min: {0}ms", test1.Min);
            Console.WriteLine("Avg: {0}ms", test1.Average);
            Console.WriteLine("Max: {0}ms", test1.Max);
            Console.WriteLine();
            Console.WriteLine("Binaryreader/writer");
            Console.WriteLine("Min: {0}ms", test2.Min);
            Console.WriteLine("Avg: {0}ms", test2.Average);
            Console.WriteLine("Max: {0}ms", test2.Max);

            Console.ReadKey();
        }
    }

    [DebuggerDisplay("Average = {Average}")]
    class SpeedTest
    {
        public int Iterations { get; set; }
        public int Times { get; set; }
        public List<Stopwatch> Watches { get; set; }
        public Action Function { get; set; }

        public long Min { get { return Watches.Min(s => s.ElapsedMilliseconds); } }
        public long Max { get { return Watches.Max(s => s.ElapsedMilliseconds); } }
        public double Average { get { return Watches.Average(s => s.ElapsedMilliseconds); } }

        public SpeedTest(Action func)
        {
            Times = 10;
            Iterations = 50000;
            Function = func;
            Watches = new List<Stopwatch>();
        }

        public void Test()
        {
            Watches.Clear();
            for (int i = 0; i < Times; i++)
            {
                var sw = Stopwatch.StartNew();
                for (int o = 0; o < Iterations; o++)
                {
                    Function();
                }
                sw.Stop();
                Watches.Add(sw);
            }
        }
    }

    public class Packtest : BasePackable
    {
        [DontPack]
        public byte Field1 = 2;
        [DontPack]
        public short Field2 = 2;
        [DontPack]
        public int Field3 = 3;
        [DontPack]
        public byte[] Field4 = new byte[50];
        [DontPack]
        public string Field5 = "Field5";

        public byte Field6 { get; set; }
        public short Field7 { get; set; }
        public int Field8 { get; set; }
        public byte[] Field9;
        public string Field10 { get; set; }

        //public List<string> Field11 { get; set; }

        public Packtest()
        {
            Field6 = 6;
            Field7 = 7;
            Field8 = 8;
            Field9 = new byte[50];
            Field10 = "Field10";
        }
    }

    public class Packtest2 : IPackable
    {
        public byte Field1 = 2;
        public short Field2 = 2;
        public int Field3 = 3;
        public byte[] Field4 = new byte[50];
        public string Field5 = "Field5";

        public byte Field6 { get; set; }
        public short Field7 { get; set; }
        public int Field8 { get; set; }
        public byte[] Field9 { get; set; }
        public string Field10 { get; set; }

        public Packtest2()
        {
            Field6 = 6;
            Field7 = 7;
            Field8 = 8;
            Field9 = new byte[50];
            Field10 = "Field10";
        }
        public void Pack(Stream stream)
        {
            stream.WriteInt8(Field6);
            stream.WriteInt16(Field7);
            stream.WriteInt32(Field8);
            stream.WriteBytes(Field9);
            stream.WriteString(Field10);
        }

        public void Unpack(Stream stream)
        {
            Field6 = stream.ReadInt8();
            Field8 = stream.ReadInt16();
            Field8 = stream.ReadInt32();
            Field9 = stream.ReadBytes();
            Field10 = stream.ReadString();
        }
    }

}


