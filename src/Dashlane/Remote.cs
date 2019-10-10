// Copyright (C) 2012-2019 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PasswordManagerAccess.Common;

namespace PasswordManagerAccess.Dashlane
{
    using R = Response;

    internal static class Remote
    {
        // TODO: Don't return JObject
        public static JObject OpenVault(string username, string deviceId, Ui ui, IRestTransport transport)
        {
            var rest = new RestClient(transport, BaseApiUrl);

            var loginType = RequestLoginType(username, rest);
            if (loginType == LoginType.DoesntExist)
                throw new BadCredentialsException("Invalid username");

            var registered = IsDeviceRegistered(username, deviceId, rest);

            // We have a registered device, no 2FA code is needed
            // TODO: Verify always-on OTP!
            if (registered && loginType != LoginType.GoogleAuth_Always)
                return Fetch(username, deviceId, rest);

            var passcode = GetPasscodeFromUser(username, loginType, ui, rest);

            // TODO: Verify what happens when OTP is not correct!
            var blob = Fetch(username, loginType, passcode.Code, rest);

            if (passcode.RememberMe)
            {
                var token = blob.GetString("token") ?? passcode.Code;
                RegisterDeviceWithPasscode(username, deviceId, DeviceName, token, rest);
            }

            return blob;
        }

        //
        // Internal
        //

        internal enum LoginType
        {
            DoesntExist,
            Regular,
            GoogleAuth_Once,
            GoogleAuth_Always,
        }

        internal static LoginType RequestLoginType(string username, RestClient rest)
        {
            var parameters = new Dictionary<string, object> {{"login", username}};
            var response = rest.PostForm<R.LoginType>(LoginTypeEndpoint, parameters);
            if (response.IsSuccessful)
            {
                string type = response.Data.Exists;
                switch (type)
                {
                case "NO":
                case "NO_UNLIKELY":
                case "NO_INVALID":
                    return LoginType.DoesntExist;
                case "YES":
                    return LoginType.Regular;
                case "YES_OTP_NEWDEVICE":
                    return LoginType.GoogleAuth_Once;
                case "YES_OTP_LOGIN":
                    return LoginType.GoogleAuth_Always;
                }

                throw new UnsupportedFeatureException($"Login type '{type}' is not supported");
            }

            throw MakeSpecializedError(response);
        }

        internal static bool IsDeviceRegistered(string username, string deviceId, RestClient rest)
        {
            var parameters = new Dictionary<string, object>
            {
                {"login", username},
                {"uki", deviceId},
            };

            var response = rest.PostForm<R.Status>(VerifyDeviceIdEndpoint, parameters);
            if (response.IsSuccessful)
                return response.Data.Code == 200 && response.Data.Message == "OK";

            throw MakeSpecializedError(response);
        }

        // Always returns a valid passcode. Throws on errors.
        internal static Ui.Passcode GetPasscodeFromUser(string username, LoginType loginType, Ui ui, RestClient rest)
        {
            // To login we need the MFA passcode. In case no MFA is set up, the code sent via email.
            Ui.Passcode passcode = Ui.Passcode.Cancel;
            switch (loginType)
            {
            case LoginType.Regular:
                do
                {
                    TriggerEmailWithPasscode(username, rest);
                    passcode = ui.ProvideEmailPasscode(0);
                } while (passcode == Ui.Passcode.Resend);
                break;
            case LoginType.GoogleAuth_Once:
            case LoginType.GoogleAuth_Always:
                passcode = ui.ProvideGoogleAuthPasscode(0);
                break;
            default:
                throw new InternalErrorException("Unknown login type");
            }

            if (passcode == Ui.Passcode.Cancel)
                throw new CanceledMultiFactorException("MFA canceled by the user");

            if (passcode.Code.IsNullOrEmpty())
                throw new InternalErrorException("MFA passcode cannot be null or blank");

            return passcode;
        }

        internal static void TriggerEmailWithPasscode(string username, RestClient rest)
        {
            var parameters = new Dictionary<string, object>
            {
                {"login", username},
                {"isOTPAware", "true"},
            };

            PerformRegisterDeviceStep(SendTokenEndpoint, parameters, rest);
        }

        internal static void RegisterDeviceWithPasscode(string username,
                                                        string deviceId,
                                                        string deviceName,
                                                        string token,
                                                        RestClient rest)
        {
            var parameters = new Dictionary<string, object>
            {
                {"devicename", deviceName},
                {"login", username},
                {"platform", "server_leeloo"},
                {"temporary", "0"},
                {"token", token},
                {"uki", deviceId},
            };

            PerformRegisterDeviceStep(RegisterEndpoint, parameters, rest);
        }

        internal static JObject Fetch(string username, string deviceId, RestClient rest)
        {
            var parameters = CommonFetchParameters(username);
            parameters["uki"] = deviceId;

            return Fetch(parameters, rest);
        }

        internal static JObject Fetch(string username, LoginType loginType, string passcode, RestClient rest)
        {
            var parameters = CommonFetchParameters(username);
            switch (loginType)
            {
            case LoginType.Regular:
                parameters["token"] = passcode;
                break;
            case LoginType.GoogleAuth_Once:
            case LoginType.GoogleAuth_Always:
                parameters["otp"] = passcode;
                break;
            default:
                throw new InternalErrorException($"Unknown login type: {loginType}");
            }

            return Fetch(parameters, rest);
        }

        internal static Dictionary<string, object> CommonFetchParameters(string username)
        {
            return new Dictionary<string, object>
            {
                {"login", username},
                {"lock", "nolock"},
                {"timestamp", "0"},
                {"sharingTimestamp", "0"},
            };
        }

        internal static JObject Fetch(Dictionary<string, object> parameters, RestClient rest)
        {
            var response = rest.PostForm(LatestEndpoint, parameters);
            if (response.IsSuccessful)
            {
                var parsed = ParseResponse(response.Content);
                CheckForErrors(parsed);

                return parsed;
            }

            throw new NetworkErrorException("Network error occurred", response.Error);
        }

        //
        // Private
        //

        private static void PerformRegisterDeviceStep(string url, Dictionary<string, object> parameters, RestClient rest)
        {
            var response = rest.PostForm(url, parameters);
            if (response.IsSuccessful && response.Content == "SUCCESS")
                return;

            throw MakeSpecializedError(response);
        }

        private static JObject ParseResponse(string response)
        {
            try
            {
                return JObject.Parse(response);
            }
            catch (JsonException e)
            {
                throw new InternalErrorException("Invalid JSON in response", e);
            }
        }

        private static void CheckForErrors(JObject response)
        {
            var error = response.SelectToken("error");
            if (error != null)
            {
                var message = error.GetString("message") ?? "Unknown error";
                throw new InternalErrorException($"Request failed with error: '{message}'");
            }

            if (response.GetString("objectType") == "message")
            {
                var message = response.GetString("content") ?? "Unknown error";
                switch (message)
                {
                case "Incorrect authentification": // Important: it's misspelled in the original code
                    // TODO: This is unlikely, since the username has been verified at this point
                    // and password is not used here. Check when this could fail.
                    throw new BadCredentialsException("Invalid username or password");
                default:
                    throw new InternalErrorException($"Request failed with error: '{message}'");
                }
            }
        }

        private static BaseException MakeSpecializedError(RestResponse response)
        {
            Uri uri = response.RequestUri;

            if (response.IsHttpError)
                return new InternalErrorException(
                    $"HTTP request to {uri} failed with status {response.StatusCode}");

            if (response.IsNetworkError)
                return new NetworkErrorException(
                    $"A network error occurred during a request to {uri}", response.Error);

            if (response.Error is JsonException)
                return new InternalErrorException(
                    $"Failed to parse JSON response from {uri}", response.Error);

            return new InternalErrorException($"Unexpected response from {uri}", response.Error);
        }

        //
        // Data
        //

        // TODO: Make this configurable
        private const string DeviceName = "password-manager-access-client";

        private const string BaseApiUrl = "https://ws1.dashlane.com/";
        private const string LoginTypeEndpoint = "7/authentication/exists";
        private const string VerifyDeviceIdEndpoint = "1/features/getForUser";
        private const string LatestEndpoint = "12/backup/latest";
        private const string SendTokenEndpoint = "6/authentication/sendtoken";
        private const string RegisterEndpoint = "6/authentication/registeruki";
    }
}
