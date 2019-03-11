// Copyright (C) 2012-2019 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using Xunit;
using PasswordManagerAccess.Common;
using PasswordManagerAccess.Keeper;

namespace PasswordManagerAccess.Test.Keeper
{
    public class ClientTest: TestBase
    {
        [Fact]
        public void OpenVault_returns_accounts()
        {
            var http = new TestHttpClient()
                .Post(GetFixture("01-kdf-info"))
                .Post(GetFixture("02-login"))
                .Post(GetFixture("03-vault"));

            var accounts = Client.OpenVault(Username, Password, http);

            Assert.NotEmpty(accounts);
        }

        [Fact]
        public void RequestKdfInfo_returns_kdf_info()
        {
            var http = new TestHttpClient()
                .Post(KdfInfoResponse)
                .ToJsonClient();
            var info = Client.RequestKdfInfo("username", http);

            Assert.Equal("c2FsdA", info.Salt);
            Assert.Equal(1337, info.Iterations);
        }

        [Fact]
        public void RequestKdfInfo_thorws_on_bad_username()
        {
            var http = new TestHttpClient()
                .Post(KdfInfoBadUsernameResponse)
                .ToJsonClient();

            Exceptions.AssertThrowsBadCredentials(
                () => Client.RequestKdfInfo("username", http),
                "username is invalid");
        }

        [Fact]
        public void Login_returns_session()
        {
            var http = new TestHttpClient()
                .Post(LoginResponse)
                .ToJsonClient();
            var session = Client.Login("username", "hash".ToBytes(), http);

            Assert.Equal("token", session.Token);
        }

        [Fact]
        public void Login_throws_on_bad_password()
        {
            var http = new TestHttpClient()
                .Post(LoginBadPasswordResponse)
                .ToJsonClient();

            Exceptions.AssertThrowsBadCredentials(
                () => Client.Login("username", "hash".ToBytes(), http),
                "password is invalid");
        }

        [Fact]
        public void RequestVault_returns_session()
        {
            var http = new TestHttpClient()
                .Post(RequestVaultResponse)
                .ToJsonClient();
            var vault = Client.RequestVault("username", "token", http);

            Assert.Empty(vault.Records);
            Assert.Empty(vault.RecordMeta);
        }

        //
        // Data
        //

        const string Username = "lastpass.ruby@gmail.com";
        const string Password = "J7wSAB&NgP!Xuo7jdSAu4KSwmcj7YZi1soHvuurd&5YBm3Y5QV!oNCi7%@xVoDZ*";

        const string KdfInfoResponse =
            @"{
                'result': 'fail',
                'result_code': 'auth_failed',

                'salt': 'c2FsdA',
                'iterations': 1337
            }";

        const string KdfInfoBadUsernameResponse =
            @"{
                'result': 'fail',
                'result_code': 'Failed_to_find_user',
                'message': ''
            }";

        const string LoginResponse =
            @"{
                'result': 'success',
                'result_code': 'auth_success',

                'session_token': 'token'
            }";

        const string LoginBadPasswordResponse = KdfInfoResponse;

        const string RequestVaultResponse =
            @"{
                'result': 'success',
                'result_code': '',

                'full_sync': 'true',
                'records': [],
                'record_meta_data': []
            }";
    }
}