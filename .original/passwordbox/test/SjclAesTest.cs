// Copyright (C) 2015 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using NUnit.Framework;

namespace PasswordBox.Test
{
    [TestFixture]
    class SjclAesTest
    {
        [Test]
        public void ComputeDoubleTable_returns_correct_result()
        {
            var table = SjclAes.ComputeDoubleTable();

            // Test data is generated with SJCL sources
            Assert.AreEqual(256, table.Length);

            Assert.AreEqual(  0, table[  0]);
            Assert.AreEqual(  2, table[  1]);
            Assert.AreEqual(  4, table[  2]);
            Assert.AreEqual(254, table[127]);
            Assert.AreEqual( 27, table[128]);
            Assert.AreEqual(231, table[254]);
            Assert.AreEqual(229, table[255]);
        }

        [Test]
        public void ComputeTrippleTable_returns_correct_result()
        {
            var table = SjclAes.ComputeTrippleTable(SjclAes.ComputeDoubleTable());

            // Test data is generated with SJCL sources
            Assert.AreEqual(256, table.Length);

            Assert.AreEqual(  0, table[  0]);
            Assert.AreEqual(246, table[  1]);
            Assert.AreEqual(247, table[  2]);
            Assert.AreEqual(220, table[127]);
            Assert.AreEqual(137, table[128]);
            Assert.AreEqual(163, table[254]);
            Assert.AreEqual( 85, table[255]);
        }

        [Test]
        public void ComputeSboxTable_returns_correct_result()
        {
            var dt = SjclAes.ComputeDoubleTable();
            var tt = SjclAes.ComputeTrippleTable(dt);
            var table = SjclAes.ComputeSboxTable(dt, tt);

            // Test data is generated with SJCL sources
            Assert.AreEqual(256, table.Length);

            Assert.AreEqual( 99, table[  0]);
            Assert.AreEqual(124, table[  1]);
            Assert.AreEqual(119, table[  2]);
            Assert.AreEqual(210, table[127]);
            Assert.AreEqual(205, table[128]);
            Assert.AreEqual(187, table[254]);
            Assert.AreEqual( 22, table[255]);

            // Every value should be exactly once
            Array.Sort(table);
            for (var i = 0; i < 256; ++i)
                Assert.AreEqual(i, table[i]);
        }

        [Test]
        public void ComputeInverseSboxTable_returns_correct_result()
        {
            var dt = SjclAes.ComputeDoubleTable();
            var tt = SjclAes.ComputeTrippleTable(dt);
            var sbox = SjclAes.ComputeSboxTable(dt, tt);
            var table = SjclAes.ComputeInverseSboxTable(sbox);

            // Test data is generated with SJCL sources
            Assert.AreEqual(256, table.Length);

            Assert.AreEqual( 82, table[  0]);
            Assert.AreEqual(  9, table[  1]);
            Assert.AreEqual(106, table[  2]);
            Assert.AreEqual(107, table[127]);
            Assert.AreEqual( 58, table[128]);
            Assert.AreEqual( 12, table[254]);
            Assert.AreEqual(125, table[255]);

            // Every value should be exactly once
            Array.Sort(table);
            for (var i = 0; i < 256; ++i)
                Assert.AreEqual(i, table[i]);
        }

        [Test]
        public void ComputeDecodeTable_returns_correct_result()
        {
            var dt = SjclAes.ComputeDoubleTable();
            var sbox = SjclAes.ComputeSboxTable(dt, SjclAes.ComputeTrippleTable(dt));
            var table = SjclAes.ComputeDecodeTable(dt, sbox);

            // Test data is generated with SJCL sources
            Assert.AreEqual(4, table.GetLength(0));
            Assert.AreEqual(256, table.GetLength(1));

            // 0
            Assert.AreEqual(1374988112, table[0,   0]);
            Assert.AreEqual(2118214995, table[0,   1]);
            Assert.AreEqual( 437757123, table[0,   2]);
            Assert.AreEqual( 337553864, table[0, 127]);
            Assert.AreEqual(1475418501, table[0, 128]);
            Assert.AreEqual(1215061108, table[0, 254]);
            Assert.AreEqual(3501741890, table[0, 255]);

            // 1
            Assert.AreEqual(1347548327, table[1,   0]);
            Assert.AreEqual(1400783205, table[1,   1]);
            Assert.AreEqual(3273267108, table[1,   2]);
            Assert.AreEqual(3356761769, table[1, 127]);
            Assert.AreEqual(2237133081, table[1, 128]);
            Assert.AreEqual(1950903388, table[1, 254]);
            Assert.AreEqual(1120974935, table[1, 255]);

            // 2
            Assert.AreEqual(2807058932, table[2,   0]);
            Assert.AreEqual(1699970625, table[2,   1]);
            Assert.AreEqual(2764249623, table[2,   2]);
            Assert.AreEqual(2848461854, table[2, 127]);
            Assert.AreEqual( 428169201, table[2, 128]);
            Assert.AreEqual(1551124588, table[2, 254]);
            Assert.AreEqual(1463996600, table[2, 255]);

            // 3
            Assert.AreEqual(4104605777, table[3,   0]);
            Assert.AreEqual(1097159550, table[3,   1]);
            Assert.AreEqual( 396673818, table[3,   2]);
            Assert.AreEqual( 514443284, table[3, 127]);
            Assert.AreEqual(4044981591, table[3, 128]);
            Assert.AreEqual(1817998408, table[3, 254]);
            Assert.AreEqual(3092726480, table[3, 255]);
        }
    }
}
