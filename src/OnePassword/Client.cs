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
            var verifiedOrMfa = VerifySessionKey(clientInfo, session, sessionKey, rest);

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
            var response = Get(rest, string.Format("v2/auth/{0}/{1}/{2}/{3}",
                                                   clientInfo.Username,
                                                   clientInfo.AccountKey.Format,
                                                   clientInfo.AccountKey.Uuid,
                                                   clientInfo.Uuid));
            var status = response.StringAt("status");
            switch (status)
            {
            case "ok":
                var session = Session.Parse(response);
                if (session.KeyUuid != clientInfo.AccountKey.Uuid)
                    throw new ClientException(ClientException.FailureReason.IncorrectCredentials,
                                              "The account key is incorrect");
                return session;
            case "device-not-registered":
                RegisterDevice(clientInfo, MakeRestClient(rest, sessionId: response.StringAt("sessionID")));
                break;
            case "device-deleted":
                ReauthorizeDevice(clientInfo, MakeRestClient(rest, sessionId: response.StringAt("sessionID")));
                break;
            default:
                throw new ClientException(
                    ClientException.FailureReason.InvalidResponse,
                    string.Format(
                        "Failed to start a new session, unsupported response status '{0}'",
                        status));
            }

            return StartNewSession(clientInfo, rest);
        }

        internal static void RegisterDevice(ClientInfo clientInfo, RestClient rest)
        {
            var response = Post(rest, "v1/device", new Dictionary<string, object>
            {
                {"uuid", clientInfo.Uuid},
                {"clientName", ClientName},
                {"clientVersion", ClientVersion},
            });

            if (response.IntAt("success") != 1)
                throw new ClientException(ClientException.FailureReason.RespondedWithError,
                                          string.Format("Failed to register the device '{0}'",
                                                        clientInfo.Uuid));
        }

        internal static void ReauthorizeDevice(ClientInfo clientInfo, RestClient rest)
        {
            var response = Put(rest, string.Format("v1/device/{0}/reauthorize", clientInfo.Uuid));

            if (response.IntAt("success") != 1)
                throw new ClientException(ClientException.FailureReason.RespondedWithError,
                                          string.Format("Failed to reauthorize the device '{0}'",
                                                        clientInfo.Uuid));
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

            public VerifyResult(VerifyStatus status): this(status, new SecondFactor[0])
            {
            }

            public VerifyResult(VerifyStatus status, SecondFactor[] factors)
            {
                Status = status;
                Factors = factors;
            }
        }

        internal static VerifyResult VerifySessionKey(ClientInfo clientInfo,
                                                      Session session,
                                                      AesKey sessionKey,
                                                      RestClient rest)
        {
            try
            {
                var response = PostEncryptedJson(
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

                var mfa = response.At("mfa", null);
                if (mfa == null)
                    return new VerifyResult(VerifyStatus.Success);

                return new VerifyResult(VerifyStatus.SecondFactorRequired, ParseSecondFactors(mfa));
            }
            catch (ClientException e)
            {
                if (!IsError102(e))
                    throw;

                throw new ClientException(ClientException.FailureReason.IncorrectCredentials,
                                          "Username, password or account key is incorrect",
                                          e.InnerException);
            }
        }

        internal static SecondFactor[] ParseSecondFactors(JToken mfa)
        {
            var factors = new List<SecondFactor>(2);

            if (mfa.BoolAt("totp/enabled", false))
                factors.Add(SecondFactor.GoogleAuthenticator);

            if (mfa.BoolAt("dsecret/enabled", false))
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
                data = new Dictionary<string, string> {{"code", code}};
                break;
            case SecondFactor.RememberMeToken:
                key = "dsecret";
                data = new Dictionary<string, string> {{"dshmac", Util.HashRememberMeToken(code, session)}};
                break;
            default:
                throw ExceptionFactory.MakeUnsupported($"2FA method {factor} is not supported");
            }

            try
            {
                var response = PostEncryptedJson("v1/auth/mfa",
                                                 new Dictionary<string, object>
                                                 {
                                                     {"sessionID", session.Id},
                                                     {"client", ClientId},
                                                     {key, data},
                                                 },
                                                 sessionKey,
                                                 rest);

                return response.StringAt("dsecret", "");
            }
            catch (ClientException e)
            {
                if (!IsError102(e))
                    throw;

                throw new ClientException(ClientException.FailureReason.IncorrectSecondFactorCode,
                                          "Incorrect second factor code",
                                          e.InnerException);
            }
        }

        internal static JObject GetAccountInfo(AesKey sessionKey, RestClient rest)
        {
            return GetEncryptedJson(
                "v1/account?attrs=billing,counts,groups,invite,me,settings,tier,user-flags,users,vaults",
                sessionKey,
                rest);
        }

        internal static JObject GetKeysets(AesKey sessionKey, RestClient rest)
        {
            return GetEncryptedJson("v1/account/keysets", sessionKey, rest);
        }

        internal static Vault[] GetVaults(JToken accountInfo,
                                          AesKey sessionKey,
                                          Keychain keychain,
                                          RestClient rest,
                                          ILogger logger)
        {
            var accessibleVaults = new HashSet<string>(BuildListOfAccessibleVaults(accountInfo));
            var allVaults = (JArray)accountInfo.At("vaults");

            if (logger != null)
            {
                var access = AbbreviateIds(accessibleVaults);
                logger.Log(DateTime.Now, $"accessible vaults: {access}");

                var noAccess = AbbreviateIds(allVaults
                    .Select(x => x.StringAt("uuid", "unknown"))
                    .Except(accessibleVaults));
                logger.Log(DateTime.Now, $"inaccessible vaults: {noAccess}");
            }

            return allVaults
                .Where(i => accessibleVaults.Contains(i.StringAt("uuid", "")))
                .Select(i => GetVault(i, sessionKey, keychain, rest, logger))
                .ToArray();
        }

        internal static string[] BuildListOfAccessibleVaults(JToken accountInfo)
        {
            const int haveReadAccess = 32;

            return accountInfo.At("me/vaultAccess")
                .Where(i => (i.IntAt("acl", 0) & haveReadAccess) != 0)
                .Select(i => i.StringAt("vaultUuid", ""))
                .Where(i => i != "")
                .ToArray();
        }

        internal static Vault GetVault(JToken json,
                                       AesKey sessionKey,
                                       Keychain keychain,
                                       RestClient rest,
                                       ILogger logger)
        {
            var id = json.StringAt("uuid");
            var attributes = Decrypt(json.At("encAttrs"), keychain);

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
            var response = Put(rest, "v1/session/signout");
            if (response.IntAt("success") != 1)
                throw new ClientException(ClientException.FailureReason.RespondedWithError,
                                          "Failed to sign out");
        }

        internal static Keychain DecryptAllKeys(JToken accountInfo, JToken keysets, ClientInfo clientInfo)
        {
            var keychain = new Keychain();
            DecryptKeysets(keysets.At("keysets"), clientInfo, keychain);
            DecryptVaultKeys(accountInfo.At("me/vaultAccess"), keychain);

            return keychain;
        }

        internal static void DecryptKeysets(JToken keysets, ClientInfo clientInfo, Keychain keychain)
        {
            var sorted = keysets
                .OrderByDescending(i => i.StringAt("encryptedBy") == MasterKeyId) // everything with "mp" goes first
                .ThenByDescending(i => i.IntAt("sn"))                             // and then is sorted by "sn"
                .ToArray();

            if (sorted[0].StringAt("encryptedBy") != MasterKeyId)
                throw ExceptionFactory.MakeInvalidOperation(
                    string.Format("Invalid keyset (key must be encrypted by '{0}')", MasterKeyId));

            var keyInfo = sorted[0].At("encSymKey");
            var masterKey = DeriveMasterKey(algorithm: keyInfo.StringAt("alg"),
                                            iterations: keyInfo.IntAt("p2c"),
                                            salt: keyInfo.StringAt("p2s").Decode64(),
                                            clientInfo: clientInfo);
            keychain.Add(masterKey);

            foreach (var i in sorted)
                DecryptKeyset(i, keychain);
        }

        internal static void DecryptVaultKeys(JToken vaults, Keychain keychain)
        {
            foreach (var i in vaults)
                DecryptAesKey(i.At("encVaultKey"), keychain);
        }

        internal static void DecryptKeyset(JToken keyset, Keychain keychain)
        {
            DecryptAesKey(keyset.At("encSymKey"), keychain);
            DecryptRsaKey(keyset.At("encPriKey"), keychain);
        }

        internal static void DecryptAesKey(JToken key, Keychain keychain)
        {
            keychain.Add(AesKey.Parse(Decrypt(key, keychain)));
        }

        internal static void DecryptRsaKey(JToken key, Keychain keychain)
        {
            keychain.Add(RsaKey.Parse(Decrypt(key, keychain)));
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

        internal static JObject GetEncryptedJson(string endpoint,
                                                 AesKey sessionKey,
                                                 RestClient rest)
        {
            return Decrypt(Get(rest, endpoint), sessionKey);
        }

        internal static JObject PostEncryptedJson(string endpoint,
                                                  Dictionary<string, object> parameters,
                                                  AesKey sessionKey,
                                                  RestClient rest)
        {
            var payload = JsonConvert.SerializeObject(parameters);
            var encryptedPayload = sessionKey.Encrypt(payload.ToBytes());
            var response = Post(rest, endpoint, encryptedPayload.ToDictionary());

            return Decrypt(response, sessionKey);
        }

        internal static JObject Decrypt(JToken json, AesKey sessionKey)
        {
            return JObject.Parse(sessionKey.Decrypt(Encrypted.Parse(json)).ToUtf8());
        }

        internal static JObject Decrypt(JToken json, Keychain keychain)
        {
            return JObject.Parse(keychain.Decrypt(Encrypted.Parse(json)).ToUtf8());
        }

        internal static string GetHttpResponse(ClientException e)
        {
            if (e.Reason != ClientException.FailureReason.NetworkError)
                return null;

            var we = e.InnerException as WebException;
            if (we == null || we.Status != WebExceptionStatus.ProtocolError)
                return null;

            var wr = we.Response as HttpWebResponse;
            if (wr == null)
                return null;

            var stream = wr.GetResponseStream();
            if (stream == null)
                return null;

            // Leave the response stream open to be able to read it again later
            using (var r = new StreamReader(stream,
                                            Encoding.UTF8,
                                            detectEncodingFromByteOrderMarks: true,
                                            bufferSize: 1024,
                                            leaveOpen: true))
            {
                var response = r.ReadToEnd();

                // Rewind it back not to make someone very confused when they try to read from it
                if (stream.CanSeek)
                    stream.Seek(0, SeekOrigin.Begin);

                return response;
            }
        }

        // This is a quite ugly attempt at handling a very special case.
        // When this specific request fails with 400, the response contains
        // the error code. It seems 102 means invalid credentials or 2FA code.
        internal static bool IsError102(ClientException e)
        {
            var response = GetHttpResponse(e);
            if (response == null)
                return false;

            try
            {
                var json = JObject.Parse(response);
                return json.IntAt("errorCode", 0) == 102;
            }
            catch (JsonException)
            {
                return false;
            }
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

        internal static JObject Get(RestClient rest, string endpoint)
        {
            var response = rest.Get(endpoint);
            if (!response.IsSuccessful)
                throw new ClientException(ClientException.FailureReason.NetworkError,
                                          $"GET request to '{endpoint}' failed",
                                          response.Error);

            try
            {
                return JObject.Parse(response.Content);
            }
            catch (JsonException e)
            {
                throw new ClientException(ClientException.FailureReason.InvalidResponse,
                                          $"Invalid JSON in response from '{endpoint}'",
                                          e);
            }
        }

        internal static JObject Post(RestClient rest, string endpoint, Dictionary<string, object> parameters)
        {
            var response = rest.PostJson(endpoint, parameters);
            if (!response.IsSuccessful)
                throw new ClientException(ClientException.FailureReason.NetworkError,
                                          $"POST request to '{endpoint}' failed",
                                          response.Error);

            try
            {
                return JObject.Parse(response.Content);
            }
            catch (JsonException e)
            {
                throw new ClientException(ClientException.FailureReason.InvalidResponse,
                                          $"Invalid JSON in response from '{endpoint}'",
                                          e);
            }
        }

        internal static JObject Put(RestClient rest, string endpoint)
        {
            var response = rest.Put(endpoint);
            if (!response.IsSuccessful)
                throw new ClientException(ClientException.FailureReason.NetworkError,
                                          $"PUT request to '{endpoint}' failed",
                                          response.Error);

            try
            {
                return JObject.Parse(response.Content);
            }
            catch (JsonException e)
            {
                throw new ClientException(ClientException.FailureReason.InvalidResponse,
                                          $"Invalid JSON in response from '{endpoint}'",
                                          e);
            }
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