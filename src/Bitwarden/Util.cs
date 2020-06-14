// Copyright (C) Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PasswordManagerAccess.Common;

namespace PasswordManagerAccess.Bitwarden
{
    internal static class Util
    {
        public static byte[] DeriveKey(string username, string password, int iterations)
        {
            return Pbkdf2.GenerateSha256(password.ToBytes(), username.ToLower().Trim().ToBytes(), iterations, 32);
        }

        public static byte[] HashPassword(string password, byte[] key)
        {
            return Pbkdf2.GenerateSha256(key, password.ToBytes(), 1, 32);
        }

        // This is the "expand" half of the "extract-expand" HKDF algorithm.
        // The length is fixed to 32 not to complicate things.
        // See https://tools.ietf.org/html/rfc5869
        public static byte[] HkdfExpand(byte[] prk, byte[] info)
        {
            return Crypto.HmacSha256(info.Concat(new byte[] { 1 }).ToArray(), prk);
        }

        public static byte[] ExpandKey(byte[] key)
        {
            var enc = HkdfExpand(key, "enc".ToBytes());
            var mac = HkdfExpand(key, "mac".ToBytes());

            return enc.Concat(mac).ToArray();
        }

        public static byte[] DecryptRsaSha1(byte[] ciphertext, byte[] privateKey)
        {
            return Crypto.DecryptRsaSha1(ciphertext, ParsePrivateKeyPkcs8(privateKey));
        }

        public static byte[] DecryptRsaSha256(byte[] ciphertext, byte[] privateKey)
        {
            return Crypto.DecryptRsaSha256(ciphertext, ParsePrivateKeyPkcs8(privateKey));
        }

        public static RSAParameters ParsePrivateKeyPkcs8(byte[] asn1)
        {
            // See: https://tools.ietf.org/html/rfc5208
            var privateKeyInfo = ExtractAsn1Item(asn1, Asn1.Kind.Sequence);
            var privateKey = privateKeyInfo.Open(reader => {
                ExtractAsn1Item(reader, Asn1.Kind.Integer); // Discard the version
                ExtractAsn1Item(reader, Asn1.Kind.Sequence); // Discard the algorithm

                return ExtractAsn1Item(reader, Asn1.Kind.Bytes);
            });
            var berEncodedPrivateKey = ExtractAsn1Item(privateKey, Asn1.Kind.Sequence);

            // See: https://tools.ietf.org/html/rfc3447#appendix-C
            return berEncodedPrivateKey.Open(reader => {
                ExtractAsn1Item(reader, Asn1.Kind.Integer); // Discard the version

                // There are occasional leading zeros that must be stripped
                Func<byte[]> readInteger =
                    () => ExtractAsn1Item(reader, Asn1.Kind.Integer).SkipWhile(i => i == 0).ToArray();

                return new RSAParameters
                {
                    Modulus = readInteger(),
                    Exponent = readInteger(),
                    D = readInteger(),
                    P = readInteger(),
                    Q = readInteger(),
                    DP = readInteger(),
                    DQ = readInteger(),
                    InverseQ = readInteger()
                };
            });
        }

        //
        // Internal
        //

        internal static byte[] ExtractAsn1Item(byte[] bytes, Asn1.Kind expectedKind)
        {
            return bytes.Open(reader => ExtractAsn1Item(reader, expectedKind));
        }

        internal static byte[] ExtractAsn1Item(BinaryReader reader, Asn1.Kind expectedKind)
        {
            var item = Asn1.ExtractItem(reader);
            if (item.Key != expectedKind)
                throw new InternalErrorException($"ASN1 decoding failed, expected {expectedKind}, got {item.Key}");

            return item.Value;
        }
    }
}
