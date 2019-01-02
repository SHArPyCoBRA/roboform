// Copyright (C) 2018 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

namespace Bitwarden
{
    public class Vault
    {
        public readonly Account[] Accounts;

        public static Vault Open(string username, string password, string deviceId, Ui ui)
        {
            return new Vault(Client.OpenVault(username, password, deviceId, ui, new HttpClient()));
        }

        //
        // Private
        //

        private Vault(Account[] accounts)
        {
            Accounts = accounts;
        }
    }
}
