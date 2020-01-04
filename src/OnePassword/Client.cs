// Copyright (C) 2012-2019 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PasswordManagerAccess.Common;
using R = PasswordManagerAccess.OnePassword.Response;

namespace PasswordManagerAccess.OnePassword
{
    public static class Client
    {
        public const string DefaultDomain = "my.1password.com";
        public const string ClientName = "1Password Extension";
        public const string ClientVersion = "20088"; // TODO: This needs to be updated every now and then.
        public const string ClientId = ClientName + "/" + ClientVersion;

        public enum Region
        {
            Global,
            Europe,
            Canada
        }

        // Public entry point to the library.
        // We try to mimic the remote structure, that's why there's an array of vaults.
        // We open all the ones we can.
        // Valid domains are: my.1password.com, my.1password.eu, my.1password.ca
        // The logger is optional, could be null
        public static Vault[] OpenAllVaults(string username,
                                            string password,
                                            string accountKey,
                                            string uuid,
                                            string domain,
                                            Ui ui,
                                            ISecureStorage storage,
                                            ILogger logger = null)
        {
            using (var transport = new RestTransport())
            {
                return OpenAllVaults(new ClientInfo(username, password, accountKey, uuid, domain),
                                     ui,
                                     storage,
                                     logger,
                                     transport);
            }
        }

        // Alternative entry point with a predefined region
        public static Vault[] OpenAllVaults(string username,
                                            string password,
                                            string accountKey,
                                            string uuid,
                                            Region region,
                                            Ui ui,
                                            ISecureStorage storage,
                                            ILogger logger = null)
        {
            return OpenAllVaults(username,
                                 password,
                                 accountKey,
                                 uuid,
                                 GetDomain(region),
                                 ui,
                                 storage,
                                 logger);
        }

        // Use this function to generate a unique random identifier for each new client.
        public static string GenerateRandomUuid()
        {
            return Util.RandomUuid();
        }

        public static string GetDomain(Region region)
        {
            switch (region)
            {
            case Region.Global:
                return "my.1password.com";
            case Region.Europe:
                return "my.1password.eu";
            case Region.Canada:
                return "my.1password.ca";
            }

            throw new ArgumentException("Region values is invalid");
        }

        //
        // Internal
        //

        // TODO: Should we make the logger a global service or a member variable not to pass it around?

        internal static Vault[] OpenAllVaults(ClientInfo clientInfo,
                                              Ui ui,
                                              ISecureStorage storage,
                                              ILogger logger,
                                              IRestTransport transport)
        {
            var rest = MakeRestClient(transport, GetApiUrl(clientInfo.Domain));

            // Step 1: Login is multi-step process in itself, which might iterate for a few times internally.
            var login = Login(clientInfo, ui, storage, rest);

            try
            {
                // Step 2: Get account info. It contains users, keys, groups, vault info and other stuff.
                //         Not the actual vault data though. That is requested separately.
                var accountInfo = GetAccountInfo(login.SessionKey, login.Rest);

                // Step 6: Get all the keysets in one place. The original code is quite hairy around this
                //         topic, so it's not very clear if these keysets should be merged with anything else
                //         or it's enough to just use these keys. For now we gonna ignore other keys and
                //         see if it's enough.
                var keysets = GetKeysets(login.SessionKey, login.Rest);

                // Step 3: Derive and decrypt keys
                var keychain = DecryptAllKeys(accountInfo, keysets, clientInfo);

                // Step 4: Get and decrypt vaults
                var vaults = GetVaults(accountInfo, login.SessionKey, keychain, login.Rest, logger);

                // Done
                return vaults;
            }
            finally
            {
                // TODO: If SignOut throws an exception it will hide the exception
                //       thrown in the try block above (if any). This will hide the
                //       original problem and thus will make it harder to diagnose
                //       the issue.

                // Last step: Make sure to sign out in any case
                SignOut(login.Rest);
            }
        }

        internal struct LoginResult
        {
            public readonly AesKey SessionKey;
            public readonly RestClient Rest;

            public LoginResult(AesKey sessionKey, RestClient rest)
            {
                SessionKey = sessionKey;
                Rest = rest;
            }
        }

        internal static LoginResult Login(ClientInfo clientInfo, Ui ui, ISecureStorage storage, RestClient rest)
        {
            while (true)
            {
                try
                {
                    return LoginAttempt(clientInfo, ui, storage, rest);
                }
                catch (ClientException e) when (e.Reason == ClientException.FailureReason.OutdatedRememberMeToken)
                {
                    // When the stored 'remember me' token is rejected by the server, we need to try the whole
                    // login sequence one more time. Probably the token is expired or it's invalid.
                }
            }
        }

        private static LoginResult LoginAttempt(ClientInfo clientInfo, Ui ui, ISecureStorage storage, RestClient rest)
        {
            // Step 1: Request to initiate a new session
            var session = StartNewSession(clientInfo, rest);

            // After a new session has been initiated, all the subsequent requests must be
            // signed with the session ID.
            rest = MakeRestClient(rest, sessionId: session.Id);

            // Step 2: Perform SRP exchange
            var sessionKey = Srp.Perform(clientInfo, session, rest);

            // Assign a request signer now that we have a key.
            // All the following requests are expected to be signed with the MAC.
            rest = MakeRestClient(rest, new MacRequestSigner(session, sessionKey), session.Id);

            // Step 3: Verify the key with the server
            var verifiedOrMfa = VerifySessionKey(session, sessionKey, rest);

            // Step 4: Submit 2FA code if needed
            if (verifiedOrMfa.Status == VerifyStatus.SecondFactorRequired)
                PerformSecondFactorAuthentication(verifiedOrMfa.Factors, session, sessionKey, ui, storage, rest);

            return new LoginResult(sessionKey, rest);
        }

        internal static string GetApiUrl(string domain)
        {
            return string.Format("https://{0}/api", domain);
        }

        internal static RestClient MakeRestClient(IRestTransport transport,
                                                  string baseUrl,
                                                  IRequestSigner signer = null,
                                                  string sessionId = null)
        {
            var headers = new Dictionary<string, string>(2) { { "X-AgileBits-Client", ClientId } };
            if (!sessionId.IsNullOrEmpty())
                headers["X-AgileBits-Session-ID"] = sessionId;

            return new RestClient(transport, baseUrl, signer, headers);
        }

        internal static RestClient MakeRestClient(RestClient rest,
                                                  IRequestSigner signer = null,
                                                  string sessionId = null)
        {
            return MakeRestClient(rest.Transport, rest.BaseUrl, signer ?? rest.Signer, sessionId);
        }

        internal static Session StartNewSession(ClientInfo clientInfo, RestClient rest)
        {
            var response = rest.Get<R.NewSession>(string.Format("v2/auth/{0}/{1}/{2}/{3}",
                                                                clientInfo.Username,
                                                                clientInfo.AccountKey.Format,
                                                                clientInfo.AccountKey.Uuid,
                                                                clientInfo.Uuid));
            if (!response.IsSuccessful)
                throw MakeError(response);

            var info = response.Data;
            var status = info.Status;
            switch (status)
            {
            case "ok":
                var session = new Session(id: info.SessionId,
                                          keyFormat: info.KeyFormat,
                                          keyUuid: info.KeyUuid,
                                          srpMethod: info.Auth.Method,
                                          keyMethod: info.Auth.Algorithm,
                                          iterations: info.Auth.Iterations,
                                          salt: info.Auth.Salt.Decode64Loose());

                if (session.KeyUuid != clientInfo.AccountKey.Uuid)
                    throw new BadCredentialsException("The account key is incorrect");
                return session;
            case "device-not-registered":
                RegisterDevice(clientInfo, MakeRestClient(rest, sessionId: info.SessionId));
                break;
            case "device-deleted":
                ReauthorizeDevice(clientInfo, MakeRestClient(rest, sessionId: info.SessionId));
                break;
            default:
                throw new InternalErrorException(
                    $"Failed to start a new session, unsupported response status '{status}'");
            }

            return StartNewSession(clientInfo, rest);
        }

        internal static void RegisterDevice(ClientInfo clientInfo, RestClient rest)
        {
            var response = rest.PostJson<R.SuccessStatus>("v1/device", new Dictionary<string, object>
            {
                {"uuid", clientInfo.Uuid},
                {"clientName", ClientName},
                {"clientVersion", ClientVersion},
            });

            if (!response.IsSuccessful)
                throw MakeError(response);

            if (response.Data.Success != 1)
                throw new InternalErrorException($"Failed to register the device '{clientInfo.Uuid}'");
        }

        internal static void ReauthorizeDevice(ClientInfo clientInfo, RestClient rest)
        {
            var response = rest.Put<R.SuccessStatus>($"v1/device/{clientInfo.Uuid}/reauthorize");

            if (!response.IsSuccessful)
                throw MakeError(response);

            if (response.Data.Success != 1)
                throw new InternalErrorException($"Failed to reauthorize the device '{clientInfo.Uuid}'");
        }

        internal enum VerifyStatus
        {
            Success,
            SecondFactorRequired
        }

        internal enum SecondFactor
        {
            GoogleAuthenticator,
            RememberMeToken
        }

        internal struct VerifyResult
        {
            public readonly VerifyStatus Status;
            public readonly SecondFactor[] Factors;

            public VerifyResult(VerifyStatus status) : this(status, new SecondFactor[0])
            {
            }

            public VerifyResult(VerifyStatus status, SecondFactor[] factors)
            {
                Status = status;
                Factors = factors;
            }
        }

        internal static VerifyResult VerifySessionKey(Session session, AesKey sessionKey, RestClient rest)
        {
            var response = PostEncryptedJson<R.VerifyKey>(
                "v2/auth/verify",
                new Dictionary<string, object>
                {
                    {"sessionID", session.Id},
                    {"clientVerifyHash", Util.CalculateClientHash(session)},
                    {"client", ClientId},
                },
                sessionKey,
                rest);

            // TODO: 1P verifies if "serverVerifyHash" is valid. Do that.
            // We assume it's all good if we got HTTP 200.

            var mfa = response.Mfa;
            if (mfa == null)
                return new VerifyResult(VerifyStatus.Success);

            return new VerifyResult(VerifyStatus.SecondFactorRequired, GetSecondFactors(mfa));
        }

        internal static SecondFactor[] GetSecondFactors(R.MfaInfo mfa)
        {
            var factors = new List<SecondFactor>(2);

            if (mfa.GoogleAuth?.Enabled == true)
                factors.Add(SecondFactor.GoogleAuthenticator);

            if (mfa.RememberMe?.Enabled == true)
                factors.Add(SecondFactor.RememberMeToken);

            if (factors.Count == 0)
                throw ExceptionFactory.MakeUnsupported("No supported 2FA methods found");

            return factors.ToArray();
        }

        internal static void PerformSecondFactorAuthentication(SecondFactor[] factors,
                                                               Session session,
                                                               AesKey sessionKey,
                                                               Ui ui,
                                                               ISecureStorage storage,
                                                               RestClient rest)
        {
            // Try "remember me" first. It's possible the server didn't allow it or
            // we don't have a valid token stored from one of the previous sessions.
            if (TrySubmitRememberMeToken(factors, session, sessionKey, storage, rest))
                return;

            var factor = ChooseInteractiveSecondFactor(factors);
            var passcode = GetSecondFactorPasscode(factor, ui);

            // Null or blank means the user canceled the 2FA
            if (passcode == null)
                throw new ClientException(ClientException.FailureReason.UserCanceledSecondFactor,
                                          "Second factor step is canceled by the user");

            var token = SubmitSecondFactorCode(factor, passcode.Code, session, sessionKey, rest);

            // Store the token with the application. Next time we're not gonna need to enter any passcodes.
            if (passcode.RememberMe)
                storage.StoreString(RememberMeTokenKey, token);
        }

        internal static bool TrySubmitRememberMeToken(SecondFactor[] factors,
                                                      Session session,
                                                      AesKey sessionKey,
                                                      ISecureStorage storage,
                                                      RestClient rest)
        {
            if (!factors.Contains(SecondFactor.RememberMeToken))
                return false;

            var token = storage.LoadString(RememberMeTokenKey);
            if (string.IsNullOrEmpty(token))
                return false;

            try
            {
                SubmitSecondFactorCode(SecondFactor.RememberMeToken, token, session, sessionKey, rest);
            }
            catch (ClientException e) when (e.Reason == ClientException.FailureReason.IncorrectSecondFactorCode)
            {
                // The token got rejected, need to erase it, it's no longer valid.
                storage.StoreString(RememberMeTokenKey, null);

                // TODO: Don't throw on this. Rather ask the user for the code and sign in as usual.
                throw new ClientException(ClientException.FailureReason.OutdatedRememberMeToken,
                                          "'Remember me' token got rejected",
                                          e.InnerException);
            }

            return true;
        }

        internal static SecondFactor ChooseInteractiveSecondFactor(SecondFactor[] factors)
        {
            if (factors.Length == 0)
                throw ExceptionFactory.MakeInvalidOperation("The list of 2FA methods could not be empty");

            // Contains is O(N) for arrays, so technically we have O(N^2) here.
            // But it's ok, since it's at most just a handful of elements. Converting
            // them to a hash set would take longer.
            foreach (var i in SecondFactorPriority)
                if (factors.Contains(i))
                    return i;

            throw ExceptionFactory.MakeInvalidOperation("The list of 2FA methods doesn't contain anything we support");
        }

        internal static Ui.Passcode GetSecondFactorPasscode(SecondFactor factor, Ui ui)
        {
            switch (factor)
            {
            case SecondFactor.GoogleAuthenticator:
                return ui.ProvideGoogleAuthPasscode();
            default:
                throw ExceptionFactory.MakeUnsupported($"2FA method {factor} is not supported");
            }
        }

        // Returns "remember me" token when successful
        internal static string SubmitSecondFactorCode(SecondFactor factor,
                                                      string code,
                                                      Session session,
                                                      AesKey sessionKey,
                                                      RestClient rest)
        {
            var key = "";
            object data = null;

            switch (factor)
            {
            case SecondFactor.GoogleAuthenticator:
                key = "totp";
                data = new Dictionary<string, string> { { "code", code } };
                break;
            case SecondFactor.RememberMeToken:
                key = "dsecret";
                data = new Dictionary<string, string> { { "dshmac", Util.HashRememberMeToken(code, session) } };
                break;
            default:
                throw ExceptionFactory.MakeUnsupported($"2FA method {factor} is not supported");
            }

            try
            {
                var response = PostEncryptedJson<R.Mfa>("v1/auth/mfa",
                                                        new Dictionary<string, object>
                                                        {
                                                            {"sessionID", session.Id},
                                                            {"client", ClientId},
                                                            {key, data},
                                                        },
                                                        sessionKey,
                                                        rest);

                return response.RememberMeToken;
            }
            catch (BadCredentialsException e)
            {
                throw new BadMultiFactorException("Incorrect second factor code", e.InnerException);
            }
        }

        internal static R.AccountInfo GetAccountInfo(AesKey sessionKey, RestClient rest)
        {
            return GetEncryptedJson<R.AccountInfo>(
                "v1/account?attrs=billing,counts,groups,invite,me,settings,tier,user-flags,users,vaults",
                sessionKey,
                rest);
        }

        internal static R.KeysetsInfo GetKeysets(AesKey sessionKey, RestClient rest)
        {
            return GetEncryptedJson<R.KeysetsInfo>("v1/account/keysets", sessionKey, rest);
        }

        internal static Vault[] GetVaults(R.AccountInfo accountInfo,
                                          AesKey sessionKey,
                                          Keychain keychain,
                                          RestClient rest,
                                          ILogger logger)
        {
            var accessibleVaults = new HashSet<string>(BuildListOfAccessibleVaults(accountInfo));
            var allVaults = accountInfo.Vaults;

            if (logger != null)
            {
                var access = AbbreviateIds(accessibleVaults);
                logger.Log(DateTime.Now, $"accessible vaults: {access}");

                var noAccess = AbbreviateIds(allVaults
                    .Select(x => x.Id)
                    .Except(accessibleVaults));
                logger.Log(DateTime.Now, $"inaccessible vaults: {noAccess}");
            }

            return allVaults
                .Where(x => accessibleVaults.Contains(x.Id))
                .Select(x => GetVault(x, sessionKey, keychain, rest, logger))
                .ToArray();
        }

        internal static string[] BuildListOfAccessibleVaults(R.AccountInfo accountInfo)
        {
            const int haveReadAccess = 32;

            return accountInfo.Me.VaultAceess
                .Where(i => (i.Acl & haveReadAccess) != 0)
                .Select(i => i.Id)
                .ToArray();
        }

        internal static Vault GetVault(R.VaultInfo vault,
                                       AesKey sessionKey,
                                       Keychain keychain,
                                       RestClient rest,
                                       ILogger logger)
        {
            var id = vault.Id;
            var attributes = Decrypt(vault.Attributes, keychain);

            return new Vault(id: id,
                             name: attributes.StringAt("name", ""),
                             description: attributes.StringAt("desc", ""),
                             accounts: GetVaultAccounts(id, sessionKey, keychain, rest, logger));
        }

        internal static Account[] GetVaultAccounts(string id,
                                                   AesKey sessionKey,
                                                   Keychain keychain,
                                                   RestClient rest,
                                                   ILogger logger)
        {
            // Convert to array right away not iterate over the same expensive enumerator again.
            var items = EnumerateAccountsItemsInVault(id, sessionKey, rest).ToArray();

            // TODO: Do we still need this? The code could be simplified when this is no longer
            // needed.
            if (logger != null)
            {
                var stats = new Dictionary<string, int>();
                foreach (var i in items)
                {
                    var key = i.StringAt("templateUuid", "unknown");
                    if (stats.ContainsKey(key))
                        stats[key]++;
                    else
                        stats.Add(key, 1);
                }

                var safeId = AbbreviateId(id);
                var text = string.Join(", ", stats.OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Value}"));

                logger.Log(DateTime.Now, $"item template count in '{safeId}': {text}");
            }

            return items
                .Where(ShouldKeepAccount)
                .Select(i => ParseAccount(i, keychain))
                .ToArray();
        }

        // Don't enumerate more than once. It's very slow since it makes network requests.
        // TODO: Add a test for the multi-batch scenario.
        internal static IEnumerable<JToken> EnumerateAccountsItemsInVault(string id,
                                                                          AesKey sessionKey,
                                                                          RestClient rest)
        {
            var batchId = 0;
            while (true)
            {
                var response = GetEncryptedJson($"v1/vault/{id}/{batchId}/items", sessionKey, rest);
                foreach (var i in response.At("items", new JArray()))
                    yield return i;

                // The last batch is marked with {batchComplete: true}
                if (response.BoolAt("batchComplete", true))
                    yield break;

                batchId = response.IntAt("contentVersion");
            }
        }

        // TODO: Add a test to verify the deleted accounts are ignored
        internal static bool ShouldKeepAccount(JToken account)
        {
            // Reject everything but accounts/logins
            if (account.StringAt("templateUuid", "") != AccountTemplateId)
                return false;

            // Reject deleted accounts (be conservative, throw only explicitly marked as "Y")
            if (account.StringAt("trashed", "") == "Y")
                return false;

            return true;
        }

        // TODO: It's really difficult to write tests for this structure: everything
        //       is encrypted and it's very annoying to create fixtures. They also look
        //       completely opaque, no clue what's going on inside. See how this could be fixed.
        internal static Account ParseAccount(JToken json, Keychain keychain)
        {
            var overview = Decrypt(json.At("encOverview"), keychain);
            var details = Decrypt(json.At("encDetails"), keychain);
            var fields = details.At("fields", new JArray());

            return new Account(json.StringAt("uuid", ""),
                               overview.StringAt("title", ""),
                               FindAccountField(fields, "username"),
                               FindAccountField(fields, "password"),
                               overview.StringAt("url", ""),
                               details.StringAt("notesPlain", ""),
                               ExtractUrls(overview),
                               ExtractFields(details));
        }

        internal static string FindAccountField(JToken json, string name)
        {
            foreach (var i in json)
                if (i.StringAt("designation", "") == name)
                    return i.StringAt("value", "");

            return "";
        }

        internal static Account.Url[] ExtractUrls(JToken overview)
        {
            return overview.At("URLs", new JArray())
                .Select(url => new Account.Url(name: url.StringAt("l", ""), value: url.StringAt("u", "")))
                .ToArray();
        }

        internal static Account.Field[] ExtractFields(JToken details)
        {
            return details.At("sections", new JArray())
                .SelectMany(ExtractSectionFields)
                .ToArray();
        }

        internal static IEnumerable<Account.Field> ExtractSectionFields(JToken section)
        {
            var name = section.StringAt("title", "");
            return section.At("fields", new JArray())
                .Select(f => new Account.Field(name: f.StringAt("t", ""),
                                               value: f.StringAt("v", ""),
                                               section: name));
        }

        internal static void SignOut(RestClient rest)
        {
            var response = rest.Put<R.SuccessStatus>("v1/session/signout");

            if (!response.IsSuccessful)
                throw MakeError(response);

            if (response.Data.Success != 1)
                throw new InternalErrorException("Failed to sign out");
        }

        internal static Keychain DecryptAllKeys(R.AccountInfo accountInfo,
                                                R.KeysetsInfo keysets,
                                                ClientInfo clientInfo)
        {
            var keychain = new Keychain();
            DecryptKeysets(keysets.Keysets, clientInfo, keychain);
            DecryptVaultKeys(accountInfo.Me.VaultAceess, keychain);

            return keychain;
        }

        internal static void DecryptKeysets(R.KeysetInfo[] keysets, ClientInfo clientInfo, Keychain keychain)
        {
            var sorted = keysets
                .OrderByDescending(x => x.EncryptedBy == MasterKeyId) // everything with "mp" goes first
                .ThenByDescending(x => x.SerialNumber)                // and then is sorted by "sn"
                .ToArray();

            if (sorted[0].EncryptedBy != MasterKeyId)
                throw ExceptionFactory.MakeInvalidOperation(
                    string.Format("Invalid keyset (key must be encrypted by '{0}')", MasterKeyId));

            var keyInfo = sorted[0].KeyOrMasterKey;
            var masterKey = DeriveMasterKey(algorithm: keyInfo.Algorithm,
                                            iterations: keyInfo.Iterations,
                                            salt: keyInfo.Salt.Decode64Loose(),
                                            clientInfo: clientInfo);
            keychain.Add(masterKey);

            foreach (var i in sorted)
                DecryptKeyset(i, keychain);
        }

        internal static void DecryptVaultKeys(R.VaultAccessInfo[] vaults, Keychain keychain)
        {
            foreach (var i in vaults)
                DecryptAesKey(i.EncryptedKey, keychain);
        }

        internal static void DecryptKeyset(R.KeysetInfo keyset, Keychain keychain)
        {
            DecryptAesKey(keyset.KeyOrMasterKey, keychain);
            DecryptRsaKey(keyset.PrivateKey, keychain);
        }

        internal static void DecryptAesKey(R.Encrypted encrypted, Keychain keychain)
        {
            keychain.Add(AesKey.Parse(Decrypt(encrypted, keychain)));
        }

        internal static void DecryptRsaKey(R.Encrypted encrypted, Keychain keychain)
        {
            keychain.Add(RsaKey.Parse(Decrypt(encrypted, keychain)));
        }

        internal static AesKey DeriveMasterKey(string algorithm,
                                               int iterations,
                                               byte[] salt,
                                               ClientInfo clientInfo)
        {
            // TODO: Check if the Unicode normalization is the correct one. This could be done
            //       by either trying to call the original JS functions in the browser console
            //       or by changing to some really weird password and trying to log in.

            var k1 = Util.Hkdf(algorithm, salt, clientInfo.Username.ToLower().ToBytes());
            var k2 = Util.Pbes2(algorithm, clientInfo.Password.Normalize(), k1, iterations);
            var key = clientInfo.AccountKey.CombineWith(k2);

            return new AesKey(MasterKeyId, key);
        }

        //
        // HTTP
        //

        internal static Common.BaseException MakeError(RestResponse response)
        {
            if (response.IsNetworkError)
                return new NetworkErrorException("Network error has occurred", response.Error);

            var serverError = ParseServerError(response.Content);
            if (serverError != null)
                return serverError;

            return new InternalErrorException(
                $"Invalid or unexpected response from the server (HTTP status: {response.StatusCode})",
                response.Error);
        }

        // Returns null when no error is found
        internal static Common.BaseException ParseServerError(string response)
        {
            try
            {
                var error = JsonConvert.DeserializeObject<R.Error>(response);
                switch (error.Code)
                {
                case 102:
                    return new BadCredentialsException("Username, password or account key is incorrect");
                default:
                    return new InternalErrorException(
                        $"The server responded with the error code {error.Code} and the message '{error.Message}'");
                }
            }
            catch (JsonException)
            {
                // Ignore, it wasn't a server error
            }

            return null;
        }

        // TODO: Remove
        private static JObject GetEncryptedJson(string endpoint,
                                                AesKey sessionKey,
                                                RestClient rest)
        {
            return Decrypt(Get<R.Encrypted>(rest, endpoint), sessionKey);
        }

        internal static T GetEncryptedJson<T>(string endpoint,
                                              AesKey sessionKey,
                                              RestClient rest)
        {
            return Decrypt<T>(Get<R.Encrypted>(rest, endpoint), sessionKey);
        }

        internal static T PostEncryptedJson<T>(string endpoint,
                                               Dictionary<string, object> parameters,
                                               AesKey sessionKey,
                                               RestClient rest)
        {
            var payload = JsonConvert.SerializeObject(parameters);
            var encryptedPayload = sessionKey.Encrypt(payload.ToBytes());
            var response = Post<R.Encrypted>(rest, endpoint, encryptedPayload.ToDictionary());

            return Decrypt<T>(response, sessionKey);
        }

        // TODO: Remove
        private static JObject Decrypt(JToken json, IDecryptor decryptor)
        {
            return JObject.Parse(decryptor.Decrypt(Encrypted.Parse(json)).ToUtf8());
        }

        // TODO: Remove
        private static JObject Decrypt(R.Encrypted encrypted, IDecryptor decryptor)
        {
            return JObject.Parse(decryptor.Decrypt(ParseEncrypted(encrypted)).ToUtf8());
        }

        internal static T Decrypt<T>(R.Encrypted encrypted, IDecryptor decryptor)
        {
            string plaintext = decryptor.Decrypt(ParseEncrypted(encrypted)).ToUtf8();
            try
            {
                return JsonConvert.DeserializeObject<T>(plaintext);
            }
            catch (JsonException e)
            {
                // When de-serialization fails it's possible that the server responded with an error
                // and we should try to parse it first.
                var serverError = ParseServerError(plaintext);
                if (serverError != null)
                    throw serverError;

                throw new InternalErrorException("Failed to parse JSON in response from the server", e);
            }
        }

        internal static Encrypted ParseEncrypted(R.Encrypted encrypted)
        {
            return new Encrypted(keyId: encrypted.KeyId,
                                 scheme: encrypted.Scheme,
                                 container: encrypted.Container,
                                 iv: encrypted.Iv?.Decode64Loose(), // This is optional
                                 ciphertext: encrypted.Ciphertext.Decode64Loose());
        }

        internal static string AbbreviateId(string id)
        {
            return id.Length > 4 ? id.Substring(0, 4) + "..." : id;
        }

        internal static string AbbreviateIds(IEnumerable<string> ids)
        {
            return string.Join(", ", ids.OrderBy(x => x).Select(x => $"'{AbbreviateId(x)}'"));
        }

        //
        // Migration helpers
        //

        private static T Get<T>(RestClient rest, string endpoint)
        {
            var response = rest.Get<T>(endpoint);
            if (!response.IsSuccessful)
                throw MakeError(response);

            return response.Data;
        }

        private static T Post<T>(RestClient rest, string endpoint, Dictionary<string, object> parameters)
        {
            var response = rest.PostJson<T>(endpoint, parameters);
            if (!response.IsSuccessful)
                throw MakeError(response);

            return response.Data;
        }

        //
        // Private
        //

        private const string MasterKeyId = "mp";
        private const string RememberMeTokenKey = "remember-me-token";
        private const string AccountTemplateId = "001";

        private static readonly SecondFactor[] SecondFactorPriority = new[]
        {
            SecondFactor.GoogleAuthenticator,
        };
    }
}
