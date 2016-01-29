// Copyright (C) 2016 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.Linq;
using NUnit.Framework;

namespace Dashlane.Test
{
    [TestFixture]
    class ParserTest
    {
        private const string Password = "password";
        private static readonly byte[] Salt = "saltsaltsaltsaltsaltsaltsaltsalt".ToBytes();
        private static readonly byte[] Content = "All your base are belong to us".ToBytes();

        [Test]
        public void ComputeEncryptionKey_returns_correct_result()
        {
            var key = Parser.ComputeEncryptionKey(Password, Salt);
            Assert.That(key, Is.EqualTo("OAIU9FREAugcAkNtoeoUithzi2qXJQc6Gfj5WgPD0mY=".Decode64()));
        }

        [Test]
        public void Sha1_computes_sha1_given_times()
        {
            var check = new Action<int, string>((iterations, expected) =>
                Assert.That(Parser.Sha1(Content, iterations), Is.EqualTo(expected.Decode64())));

            check(0, Convert.ToBase64String(Content));
            check(1, "xgmXgTCENlJpbnSLucn3NwPXkIk=");
            check(5, "RqcjtwJ5KY1MON7n3WwvqGhrrpg=");
        }

        [Test]
        public void DeriveEncryptionKeyAndIv_computes_key_and_iv_for_given_number_of_iterations()
        {
            var key = "OAIU9FREAugcAkNtoeoUithzi2qXJQc6Gfj5WgPD0mY=".Decode64();
            var check = new Action<int, string, string>((iterations, expectedKey, expectedIv) =>
            {
                var keyIv = Parser.DeriveEncryptionKeyAndIv(key, Salt, iterations);
                Assert.That(keyIv.Key, Is.EqualTo(expectedKey.Decode64()));
                Assert.That(keyIv.Iv, Is.EqualTo(expectedIv.Decode64()));
            });

            check(1, "6HA2Rq9GTeKzAc1imNjvyaXBGW4zRA5wIr60Vbx/o8w=", "fCk2EkpIYGn05JHcVfR8eQ==");
            check(5, "fsuGfEOoYL4uOmp24ZuAExIuVePh6YIu7t0rfCDogpM=", "/vsHfrsRzyGCQOBP4UEQuw==");
        }

        [Test]
        public void DecryptAes256_decrypts_ciphertext()
        {
            Assert.That(
                Parser.DecryptAes256(
                    "TZ1+if9ofqRKTatyUaOnfudletslMJ/RZyUwJuR/+aI=".Decode64(),
                    "YFuiAVZgOD2K+s6y8yaMOw==".Decode64(),
                    "OfOUvVnQzB4v49sNh4+PdwIFb9Fr5+jVfWRTf+E2Ghg=".Decode64()),
                Is.EqualTo(Content));
        }

        [Test]
        public void Inflate_decompresses_data()
        {
            Assert.That(
                Parser.Inflate("c8zJUajMLy1SSEosTlVILEpVSErNyc9LVyjJVygtBgA=".Decode64()),
                Is.EqualTo(Content));
        }

        [Test]
        public void ParseEncryptedBlob_parses_kwc3_blob()
        {
            var blob = Salt.Concat("KWC3".ToBytes()).Concat(Content).ToArray();
            var parsed = Parser.ParseEncryptedBlob(blob);

            Assert.That(parsed.Ciphertext, Is.EqualTo(Content));
            Assert.That(parsed.Salt, Is.EqualTo(Salt));
            Assert.That(parsed.Compressed, Is.True);
            Assert.That(parsed.UseDerivedKey, Is.False);
            Assert.That(parsed.Iterations, Is.EqualTo(1));
        }

        [Test]
        public void ParseEncryptedBlob_parses_legacy_blob()
        {
            var blob = Salt.Concat(Content).ToArray();
            var parsed = Parser.ParseEncryptedBlob(blob);

            Assert.That(parsed.Ciphertext, Is.EqualTo(Content));
            Assert.That(parsed.Salt, Is.EqualTo(Salt));
            Assert.That(parsed.Compressed, Is.False);
            Assert.That(parsed.UseDerivedKey, Is.True);
            Assert.That(parsed.Iterations, Is.EqualTo(5));
        }

        [Test]
        public void ParseEncryptedBlob_parses_short_blob_as_legacy()
        {
            var ciphertext = new byte[] {13, 37};
            var blob = Salt.Concat(ciphertext).ToArray();
            var parsed = Parser.ParseEncryptedBlob(blob);

            Assert.That(parsed.Ciphertext, Is.EqualTo(ciphertext));
            Assert.That(parsed.Salt, Is.EqualTo(Salt));
            Assert.That(parsed.Compressed, Is.False);
            Assert.That(parsed.UseDerivedKey, Is.True);
            Assert.That(parsed.Iterations, Is.EqualTo(5));
        }

        [Test]
        [ExpectedException(typeof(ArgumentException), ExpectedMessage = "Blob is too short\r\nParameter name: blob")]
        public void ParseEncryptedBlob_throws_on_too_short_blob()
        {
            Parser.ParseEncryptedBlob(new byte[] {13, 37});
        }
    }
}