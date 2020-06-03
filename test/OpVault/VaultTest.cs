// Copyright (C) Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using PasswordManagerAccess.OpVault;
using Xunit;

namespace PasswordManagerAccess.Test.OpVault
{
    public class VaultTest
    {
        [Fact]
        public void Open_returns_accounts()
        {
            var accounts = Vault.Open(TestVaultPath, Password);
            Assert.Equal(3, accounts.Length);
        }

        [Fact]
        public void Open_supprts_nested_folders()
        {
            var accounts = Vault.Open(TestVaultPath, Password);
            var childFolder = accounts.First(i => i.Folder.Name == "Even Cooler Stuff").Folder;

            Assert.Equal("Cool Stuff", childFolder.Parent.Name);
        }

        [Fact]
        public void Open_throws_on_invalid_path()
        {
            var e = Assert.Throws<ParseException>(() => Vault.Open("does/not/exist", Password));
            Assert.Contains("doesn't exist", e.Message);
        }

        [Fact]
        public void Open_throws_on_incorrect_password()
        {
            var e = Assert.Throws<ParseException>(() => Vault.Open(TestVaultPath, "incorrect password"));
            Assert.Contains("password is incorrect", e.Message);
        }

        [Fact]
        public void LoadProfile_reads_profile_js()
        {
            var profile = Vault.LoadProfile(TestVaultPath);
            Assert.Equal("714A14D7017048CC9577AD050FC9C6CA", (string)profile["uuid"]);
        }

        [Fact]
        public void LoadFolders_reads_folders_js()
        {
            var folders = Vault.LoadFolders(TestVaultPath);
            Assert.Equal(3, folders.Length);
        }

        [Fact]
        public void LoadItems_reads_items_from_all_bands()
        {
            var items = Vault.LoadItems(TestVaultPath);
            Assert.Equal(3, items.Length);
        }

        [Fact]
        public void LoadJsAsJson_reads_json_from_file()
        {
            var json = Vault.LoadJsAsJson(string.Format("{0}/default/profile.js", TestVaultPath), "var profile=", ";");
            Assert.Equal("714A14D7017048CC9577AD050FC9C6CA", (string)json["uuid"]);
        }

        [Fact]
        public void LoadJsAsJsonFromString_returns_parsed_json_object()
        {
            var expected = JObject.Parse("{'key': 'value'}");
            var json = Vault.LoadJsAsJsonFromString("var j = {'key': 'value'};", "var j = ", ";");

            Assert.True(JToken.DeepEquals(json, expected));
        }

        [Fact]
        public void LoadJsAsJsonFromString_throws_on_too_short_input()
        {
            var e = Assert.Throws<ParseException>(() => Vault.LoadJsAsJsonFromString("-", "var j = ", ";"));
            Assert.Contains("too short", e.Message);
        }

        [Fact]
        public void LoadJsAsJsonFromString_throws_on_missing_prefix()
        {
            var e = Assert.Throws<ParseException>(() => Vault.LoadJsAsJsonFromString("var j = {};", "-", ";"));
            Assert.Contains("prefix is not found", e.Message);
        }

        [Fact]
        public void LoadJsAsJsonFromString_throws_on_missing_suffix()
        {
            var e = Assert.Throws<ParseException>(() => Vault.LoadJsAsJsonFromString("var j = {};", "var j =", "-"));
            Assert.Contains("suffix is not found", e.Message);
        }

        [Fact]
        public void MakeFilename_makes_path_inside_vault()
        {
            var expected = Path.DirectorySeparatorChar switch
            {
                '/' => "path/to/test.opvault/default/profile.js",
                '\\' => "path\\to\\test.opvault\\default\\profile.js",
                _ => throw new InvalidOperationException("Unknown directory separator"),
            };
            var filename = Vault.MakeFilename("path/to/test.opvault", "profile.js");

            Assert.Equal(expected, filename);
        }

        [Fact]
        public void MakeFilename_ignores_extra_slash()
        {
            var expected = Path.DirectorySeparatorChar switch
            {
                '/' => "path/to/test.opvault/default/profile.js",
                '\\' => "path\\to\\test.opvault\\default\\profile.js",
                _ => throw new InvalidOperationException("Unknown directory separator"),
            };
            var filename = Vault.MakeFilename("path/to/test.opvault/", "profile.js");

            Assert.Equal(expected, filename);
        }

        [Fact]
        public void MakeFilename_keeps_drive_letter()
        {
            var expected = Path.DirectorySeparatorChar switch
            {
                '/' => "c:/path/to/test.opvault/default/profile.js",
                '\\' => "c:\\path\\to\\test.opvault\\default\\profile.js",
                _ => throw new InvalidOperationException("Unknown directory separator"),
            };
            var filename = Vault.MakeFilename("c:/path/to/test.opvault", "profile.js");

            Assert.Equal(expected, filename);
        }

        [Fact]
        public void NormalizeSlashes_converts_forward_to_back_slashes_on_windows()
        {
            // This test is only valid on Windows, there's no slash conversion on Linux/Mac
            if (Path.DirectorySeparatorChar == '\\')
            {
                var normalized = Vault.NormalizeSlashes("/path/to\\a/file/");
                Assert.Equal("\\path\\to\\a\\file\\", normalized);
            }
        }

        //
        // Data
        //

        private const string TestVaultPath = "OpVault/Fixtures/test.opvault";
        private const string Password = "password";
    }
}