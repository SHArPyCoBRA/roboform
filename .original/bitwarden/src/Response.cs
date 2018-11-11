// Copyright (C) 2018 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using Newtonsoft.Json;

namespace Bitwarden
{
    internal static class Response
    {
        [JsonObject(ItemRequired = Required.Always)]
        internal struct KdfInfo
        {
            public int Kdf;
            public int KdfIterations;
        }

        [JsonObject(ItemRequired = Required.Always)]
        internal struct AuthToken
        {
            [JsonProperty(PropertyName = "token_type")]
            public string TokenType;
            [JsonProperty(PropertyName = "access_token")]
            public string AccessToken;
        }

        [JsonObject(ItemRequired = Required.Always)]
        internal struct Vault
        {
            public Profile Profile;
            public Item[] Ciphers;
        }

        internal struct Profile
        {
            public string Key;
        }

        internal enum ItemType
        {
            Login = 1,
            SecureNote = 2,
            Card = 3,
            Identity = 4,
        }

        internal struct Item
        {
            [JsonProperty(Required = Required.Always)]
            public ItemType Type;

            public string Id;
            public string Name;
            public string Notes;
            public LoginInfo Login;
        }

        internal struct LoginInfo
        {
            public string Username;
            public string Password;
            public string Uri;
        }
    }
}