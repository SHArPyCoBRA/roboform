// Copyright (C) 2018 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using NUnit.Framework;

namespace Bitwarden.Test
{
    [TestFixture]
    public class CipherStringTest
    {
        [Test]
        public void Parse_handles_default_cipher_mode()
        {
            var cs = CipherString.Parse("aXZpdml2aXZpdml2aXZpdg==|Y2lwaGVydGV4dA==");

            Assert.That(cs.Mode, Is.EqualTo(CipherMode.Aes256Cbc));
            Assert.That(cs.Iv, Is.EqualTo(Iv));
            Assert.That(cs.Ciphertext, Is.EqualTo(Ciphertext));
        }

        [Test]
        public void Parse_handles_cipher_mode_0()
        {
            var cs = CipherString.Parse("0.aXZpdml2aXZpdml2aXZpdg==|Y2lwaGVydGV4dA==");

            Assert.That(cs.Mode, Is.EqualTo(CipherMode.Aes256Cbc));
            Assert.That(cs.Iv, Is.EqualTo(Iv));
            Assert.That(cs.Ciphertext, Is.EqualTo(Ciphertext));
        }

        [Test]
        public void Parse_handles_cipher_mode_1()
        {
            var cs = CipherString.Parse("1.aXZpdml2aXZpdml2aXZpdg==|Y2lwaGVydGV4dA==|bWFjIG1hYyBtYWMgbWFjIG1hYyBtYWMgbWFjIG1hYyA=");

            Assert.That(cs.Mode, Is.EqualTo(CipherMode.Aes128CbcHmacSha256));
            Assert.That(cs.Iv, Is.EqualTo(Iv));
            Assert.That(cs.Ciphertext, Is.EqualTo(Ciphertext));
            Assert.That(cs.Mac, Is.EqualTo(Mac));
        }

        [Test]
        public void Parse_handles_cipher_mode_2()
        {
            var cs = CipherString.Parse("2.aXZpdml2aXZpdml2aXZpdg==|Y2lwaGVydGV4dA==|bWFjIG1hYyBtYWMgbWFjIG1hYyBtYWMgbWFjIG1hYyA=");

            Assert.That(cs.Mode, Is.EqualTo(CipherMode.Aes256CbcHmacSha256));
            Assert.That(cs.Iv, Is.EqualTo(Iv));
            Assert.That(cs.Ciphertext, Is.EqualTo(Ciphertext));
            Assert.That(cs.Mac, Is.EqualTo(Mac));
        }

        [Test]
        public void Parse_throws_on_malformed_input()
        {
            var invalid = new[] {"", "0.", "0..", "0.|||"};
            foreach (var i in invalid)
                VerifyThrowsInvalidFormat(i, "Invalid/unsupported cipher string format");
        }

        [Test]
        public void Parse_throws_on_invalid_cipher_mode()
        {
            var invalid = new[] {"3.", "A."};
            foreach (var i in invalid)
                VerifyThrowsInvalidFormat(i, "Invalid/unsupported cipher mode");
        }

        [Test]
        public void Properties_are_set()
        {
            var mode = CipherMode.Aes256CbcHmacSha256;
            var cs = new CipherString(mode, Iv, Ciphertext, Mac);

            Assert.That(cs.Mode, Is.EqualTo(mode));
            Assert.That(cs.Iv, Is.EqualTo(Iv));
            Assert.That(cs.Ciphertext, Is.EqualTo(Ciphertext));
            Assert.That(cs.Mac, Is.EqualTo(Mac));
        }

        [Test]
        public void Ctor_throws_on_invalid_parameters()
        {
            VerifyThrowsInvalidFormat(mode: CipherMode.Aes256Cbc,
                                      iv: null,
                                      ciphertext: Ciphertext,
                                      mac: null,
                                      expectedMessage: "IV must be 16 bytes long");

            VerifyThrowsInvalidFormat(mode: CipherMode.Aes256Cbc,
                                      iv: "invalid iv".ToBytes(),
                                      ciphertext: Ciphertext,
                                      mac: null,
                                      expectedMessage: "IV must be 16 bytes long");

            VerifyThrowsInvalidFormat(mode: CipherMode.Aes256Cbc,
                                      iv: Iv,
                                      ciphertext: null,
                                      mac: null,
                                      expectedMessage: "Ciphertext must not be null");

            VerifyThrowsInvalidFormat(mode: CipherMode.Aes256Cbc,
                                      iv: Iv,
                                      ciphertext: Ciphertext,
                                      mac: Mac,
                                      expectedMessage: "MAC is not supported in AES-256-CBC mode");

            VerifyThrowsInvalidFormat(mode: CipherMode.Aes256CbcHmacSha256,
                                      iv: Iv,
                                      ciphertext: Ciphertext,
                                      mac: null,
                                      expectedMessage : "MAC must be 32 bytes long");

            VerifyThrowsInvalidFormat(mode: CipherMode.Aes256CbcHmacSha256,
                                      iv: Iv,
                                      ciphertext: Ciphertext,
                                      mac: "invalid mac".ToBytes(),
                                      expectedMessage : "MAC must be 32 bytes long");
        }

        //
        // Helper
        //

        private static void VerifyThrowsInvalidFormat(string input, string expectedMessage)
        {
            Assert.That(() => CipherString.Parse(input),
                        Throws.InstanceOf<ClientException>()
                            .And.Message.Contains(expectedMessage)
                            .And.Property("Reason")
                            .EqualTo(ClientException.FailureReason.InvalidFormat));
        }

        private static void VerifyThrowsInvalidFormat(CipherMode mode,
                                                      byte[] iv,
                                                      byte[] ciphertext,
                                                      byte[] mac,
                                                      string expectedMessage)
        {
            Assert.That(() => new CipherString(mode, iv, ciphertext, mac),
                        Throws.InstanceOf<ClientException>()
                            .And.Message.Contains(expectedMessage)
                            .And.Property("Reason")
                            .EqualTo(ClientException.FailureReason.InvalidFormat));
        }

        //
        // Data
        //

        private static readonly byte[] Iv = "iviviviviviviviv".ToBytes();
        private static readonly byte[] Ciphertext = "ciphertext".ToBytes();
        private static readonly byte[] Mac = "mac mac mac mac mac mac mac mac ".ToBytes();
    }
}
