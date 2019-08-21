// Copyright (C) 2012-2019 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Moq;
using PasswordManagerAccess.Dashlane;
using Xunit;

namespace PasswordManagerAccess.Test.Dashlane
{
    public class VaultTest
    {
        public const string Username = "username";
        public const string Password = "password";
        public const string Uki = "uki";

        public const string Dude = "dude.com";
        public const string Nam = "nam.com";

        [Fact]
        public void Open_opens_empty_vault()
        {
            Assert.Empty(Accounts("empty-vault"));
        }

        [Fact]
        public void Open_opens_a_vault_with_empty_fullfile_and_one_add_transaction()
        {
            Assert.Equal(new[]{Dude}, Accounts("empty-fullfile-one-add-transaction"));
        }

        [Fact]
        public void Open_opens_a_vault_with_empty_fullfile_and_two_add_transations()
        {
            Assert.Equal(new[]{Dude, Nam}, Accounts("empty-fullfile-two-add-transactions"));
        }

        [Fact]
        public void Open_opens_a_vault_with_empty_fullfile_and_two_add_and_one_remove_transations()
        {
            Assert.Equal(new[]{Dude, Nam}, Accounts("empty-fullfile-two-add-one-remove-transactions"));
        }

        [Fact]
        public void Open_opens_a_vault_with_two_accounts_in_fullfile()
        {
            Assert.Equal(new[]{Dude, Nam}, Accounts("two-accounts-in-fullfile"));
        }

        [Fact]
        public void Open_opens_a_vault_with_two_accounts_in_fullfile_and_one_remove_transaction()
        {
            Assert.Equal(new[]{Dude}, Accounts("two-accounts-in-fullfile-one-remove-transaction"));
        }

        [Fact]
        public void Open_opens_a_vault_with_two_accounts_in_fullfile_and_two_remove_transactions()
        {
            Assert.Empty(Accounts("two-accounts-in-fullfile-two-remove-transactions"));
        }

        [Fact]
        public void Open_opens_a_vault_with_two_accounts_in_fullfile_and_two_remove_and_one_add_transactions()
        {
            Assert.Equal(new[]{Dude}, Accounts("two-accounts-in-fullfile-two-remove-one-add-transactions"));
        }

        //
        // Helpers
        //

        private static string[] Accounts(string filename)
        {
            return Vault.Open(Username, Password, Uki, SetupWebClient(filename))
                .Accounts
                .Select(i => i.Name)
                .ToArray();
        }

        private static IWebClient SetupWebClient(string filename)
        {
            var webClient = new Mock<IWebClient>();
            webClient
                .Setup(x => x.UploadValues(It.IsAny<string>(), It.IsAny<NameValueCollection>()))
                .Returns(File.ReadAllBytes(string.Format("Fixtures/{0}.json", filename)));

            return webClient.Object;
        }
    }
}
