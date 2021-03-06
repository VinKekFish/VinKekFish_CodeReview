using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using vinkekfish;
using keccak;   // keccak взят отсюда https://github.com/fdsc/old/releases
using BytesBuilder = cryptoprime.BytesBuilder;
using System.Runtime.CompilerServices;
using alien_SkeinFish;
using cryptoprime;
using CodeGenerated.Cryptoprimes;

namespace main_tests
{
    class ThreeFishGenTestByBits
    {
        readonly TestTask task;
        public ThreeFishGenTestByBits(ConcurrentQueue<TestTask> tasks)
        {
            task = new TestTask("ThreeFishGenTestByBits", StartTests);
            tasks.Enqueue(task);

            sources = SourceTask.getIterator();
        }

        class SourceTask
        {
            public string Key;
            public byte[][] Value;

            public static IEnumerable<SourceTask> getIterator()
            {
                // 128 - это размер одного блока
                long size = 128;
                for (ulong valk = 0; valk < (ulong) (size << 3); valk++)
                {
                    var bk1 = new byte[size];
                    BytesBuilder.ToNull(bk1, 0xFFFF_FFFF__FFFF_FFFF);
                    BitToBytes.resetBit(bk1, valk);

                    var bk2 = new byte[size];
                    BytesBuilder.ToNull(bk2);
                    BitToBytes.setBit(bk2, valk);

                    for (ulong valt = 0; valt < (ulong) (size << 3); valt++)
                    {
                        var b1 = new byte[size];
                        BytesBuilder.ToNull(b1, 0xFFFF_FFFF__FFFF_FFFF);
                        BitToBytes.resetBit(b1, valt);

                        var b2 = new byte[size];
                        BytesBuilder.ToNull(b2);
                        BitToBytes.setBit(b2, valt);

                        yield return new SourceTask() {Key = "Threfish with valk = " + valk, Value = new byte[][] {bk1, b1}};
                        yield return new SourceTask() {Key = "Threfish with valk = " + valk, Value = new byte[][] {bk2, b2}};
                    }
                }

                yield break;
            }
        }

        readonly IEnumerable<SourceTask> sources = null;

        public unsafe void StartTests()
        {
            foreach (var ts in sources)
            {
                TestForKeyAndText (ts);
                TestForKeyAndTweak(ts);
            }
        }

        private unsafe void TestForKeyAndText(SourceTask ts)
        {
            var s0 = BytesBuilder.CloneBytes(ts.Value[0]);
            var s1 = BytesBuilder.CloneBytes(ts.Value[1]);
            var bn = new byte[128];

            byte[] h1 = new byte[128], h2;
            var tft = new ThreefishTransform(ts.Value[0], bn, ThreefishTransformMode.Encrypt);
            var tw = new ulong[2];
            tft.SetTweak(tw);
            h1 = (byte[])ts.Value[1].Clone();
            tft.TransformBlock(h1, 0, 128, h1, 0);

            var twg = new byte[16];
            var tfg = new cryptoprime.Threefish1024(ts.Value[0], twg);
            h2 = (byte[])ts.Value[1].Clone();
            fixed (ulong * key = tfg.key, tweak = tfg.tweak)
            fixed (byte * h1b = h2)
            {
                ulong * h1u = (ulong *) h1b;
                Threefish_Static_Generated.Threefish1024_step(key, tweak, h1u);
                //threefish_slowly.UlongToBytes(threefish_slowly.Encrypt(b0, tw, b1), null);
            }

            if (!BytesBuilder.UnsecureCompare(s0, ts.Value[0]))
            {
                task.error.Add(new Error() { Message = "Sources arrays has been changed for test array: " + ts.Key });
            }

            if (!BytesBuilder.UnsecureCompare(h1, h2))
            {
                task.error.Add(new Error() { Message = "Hashes are not equal for test array: " + ts.Key });
            }
        }

        private unsafe void TestForKeyAndTweak(SourceTask ts)
        {
            var s0 = BytesBuilder.CloneBytes(ts.Value[0]);
            var s1 = BytesBuilder.CloneBytes(ts.Value[1]);
            var bn = new byte[128];

            byte[] h1 = new byte[128], h2;
            var tft = new ThreefishTransform(ts.Value[0], bn, ThreefishTransformMode.Encrypt);
            var tw = new ulong[2];

            var b = threefish_slowly.BytesToUlong(ts.Value[1]);
            tw[0] = b[0] ^ b[1];
            tw[1] = b[2] ^ b[3];
            tft.SetTweak(tw);

            tft.TransformBlock(h1, 0, 128, h1, 0);

            var input = new ulong[16];
            tw = new ulong[2];
            var b0 = threefish_slowly.BytesToUlong(ts.Value[0]);
            var b1 = threefish_slowly.BytesToUlong(ts.Value[1]);
            tw[0] = b[0] ^ b[1];
            tw[1] = b[2] ^ b[3];
            h2 = threefish_slowly.UlongToBytes(threefish_slowly.Encrypt(b0, tw, input), null);

            // h2 = new SHA3(1024).getHash512(s);

            if (!BytesBuilder.UnsecureCompare(s0, ts.Value[0]))
            {
                task.error.Add(new Error() { Message = "Sources arrays has been changed for test array: " + ts.Key });
            }

            if (!BytesBuilder.UnsecureCompare(h1, h2))
            {
                task.error.Add(new Error() { Message = "Hashes are not equal for test array: " + ts.Key });
            }
        }
    }
}
