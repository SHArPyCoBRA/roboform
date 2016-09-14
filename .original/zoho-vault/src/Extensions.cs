// Copyright (C) 2016 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ZohoVault
{
    static class Extensions
    {
        //
        // string
        //

        public static byte[] ToBytes(this string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        //
        // byte[]
        //

        public static string ToUtf8(this byte[] x)
        {
            return Encoding.UTF8.GetString(x);
        }

        //
        // JObject
        //

        public static JToken At(this JObject j, string path)
        {
            var c = j.Root;
            foreach (var i in path.Split('/'))
            {
                if (c.Type != JTokenType.Object)
                    throw new ArgumentException("Must be nested objects all the way down");

                c = c[i];
                if (c == null)
                    throw new ArgumentException("Path doesn't exist", path);

            }

            return c;
        }

        public static string StringAt(this JObject j, string path)
        {
            var s = j.At(path);
            if (s.Type != JTokenType.String)
                throw new ArgumentException("The value is not a string");

            return (string)s;
        }

        public static int IntAt(this JObject j, string path)
        {
            var s = j.At(path);
            if (s.Type != JTokenType.Integer)
                throw new ArgumentException("The value is not an integer");

            return (int)s;
        }
    }
}
