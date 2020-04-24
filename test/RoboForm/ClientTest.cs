// Copyright (C) Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System.Collections.Generic;
using System.Net;
using PasswordManagerAccess.Common;
using PasswordManagerAccess.RoboForm;
using Xunit;

namespace PasswordManagerAccess.Test.RoboForm
{
    public class ClientTest
    {
        [Fact]
        public void Logout_makes_POST_request_to_specific_url()
        {
            var rest = new RestFlow()
                .Post("")
                    .ExpectUrl($"https://online.roboform.com/rf-api/{TestData.Username}?logout");

            Client.Logout(TestData.Username, Session, rest.ToRestClient(""));
        }

        [Fact]
        public void Logout_throws_on_not_HTTP_OK()
        {
            var rest = new RestFlow()
                .Post("", HttpStatusCode.NotFound);

            Exceptions.AssertThrowsInternalError(() => Client.Logout(TestData.Username, Session, rest), "404");
        }

        [Fact(Skip = "RestFlow doesn't support binary responses yet")]
        public void GetBlob_returns_received_bytes()
        {
            var expected = "Blah, blah, blah...";
            var rest = new RestFlow()
                .Get(expected);

            var blob = Client.GetBlob(TestData.Username, Session, rest);

            Assert.Equal(expected.ToBytes(), blob);
        }

        [Fact]
        public void GetBlob_makes_GET_request_to_specific_url()
        {
            var rest = new RestFlow()
                .Get("")
                    .ExpectUrl($"https://online.roboform.com/rf-api/{TestData.Username}/user-data.rfo");

            Client.GetBlob(TestData.Username, Session, rest.ToRestClient(""));
        }

        [Fact]
        public void GetBlob_throws_on_not_HTTP_OK()
        {
            var rest = new RestFlow()
                            .Get("", HttpStatusCode.NotFound);

            Exceptions.AssertThrowsInternalError(() => Client.GetBlob(TestData.Username, Session, rest), "404");
        }

        [Fact(Skip = "RestFlow doesn't support response headers")]
        public void Step1_makes_POST_request_to_specific_url_with_headers()
        {
            var rest = new RestFlow()
                .Post("", HttpStatusCode.Unauthorized)
                    .ExpectUrl($"https://online.roboform.com/rf-api/{TestData.Username}?login")
                    .ExpectHeader("Authorization", "SibAuth realm=");

            Client.Step1(TestData.Credentials, new Client.OtpOptions(), rest.ToRestClient(""));
        }

        [Fact(Skip = "RestFlow doesn't support response headers")]
        public void Step1_returns_WWW_Authenticate_header()
        {
            // TODO: Implement
        }

        [Fact(Skip = "RestFlow doesn't support response headers")]
        public void Step1_throws_on_missing_WWW_Authenticate_header()
        {
            // TODO: Implement
        }

        [Fact]
        public void Step2_makes_POST_request_to_specific_url_and_headers_set()
        {
            var rest = new RestFlow()
                .Post("", cookies: Step2Cookies)
                    .ExpectUrl($"https://online.roboform.com/rf-api/{TestData.Username}?login")
                    /*.ExpectHeader("Authorization", "SibAuth sid=")*/;  // TODO: Add support for partial header match

            Client.Step2(TestData.Credentials, new Client.OtpOptions(), TestData.AuthInfo, rest.ToRestClient(""));
        }

        [Fact]
        public void Step2_makes_POST_request_with_channel_set_to_dash_when_no_MFA_present()
        {
            var rest = new RestFlow()
                .Post("", cookies: Step2Cookies)
                    .ExpectHeader("x-sib-auth-alt-channel", "-");

            Client.Step2(TestData.Credentials, new Client.OtpOptions(), TestData.AuthInfo, rest);
        }

        [Fact]
        public void Step2_makes_POST_request_with_x_sib_headers_set_when_MFA_is_present()
        {
            var rest = new RestFlow()
                .Post("", cookies: Step2Cookies)
                    .ExpectHeader("x-sib-auth-alt-channel", "channel")
                    .ExpectHeader("x-sib-auth-alt-otp", "otp")
                    .ExpectHeader("x-sib-auth-alt-memorize", "1");

            Client.Step2(TestData.Credentials,
                         new Client.OtpOptions("channel", "otp", true),
                         TestData.AuthInfo,
                         rest);
        }

        [Fact]
        public void Step2_returns_cookies()
        {
            var rest = new RestFlow()
                .Post("", cookies: Step2Cookies);

            var result = Client.Step2(TestData.Credentials, new Client.OtpOptions(), TestData.AuthInfo, rest);

            AsserSessionsAretEqual(result.Session, Session);
        }

        [Fact]
        public void Step2_ignores_extra_cookies()
        {
            var extraCookies = new Dictionary<string, string> { ["blah"] = "blah-blah" };
            var rest = new RestFlow()
                .Post("", cookies: Step2Cookies.MergeCopy(extraCookies));

            var result = Client.Step2(TestData.Credentials, new Client.OtpOptions(), TestData.AuthInfo, rest);

            AsserSessionsAretEqual(result.Session, Session);
        }

        [Fact]
        public void Step2_throws_on_missing_cookies()
        {
            var rest = new RestFlow()
                .Post("", cookies: new Dictionary<string, string>());

            Exceptions.AssertThrowsInternalError(
                () => Client.Step2(TestData.Credentials, new Client.OtpOptions(), TestData.AuthInfo, rest),
                "cookie wasn't found in the response");
        }

        [Fact]
        public void Step2_throws_on_HTTP_unuthorized()
        {
            var rest = new RestFlow()
                .Post("", HttpStatusCode.Unauthorized);

            Exceptions.AssertThrowsBadCredentials(
                () => Client.Step2(TestData.Credentials, new Client.OtpOptions(), TestData.AuthInfo, rest),
                "Invalid username or password");
        }

        [Fact]
        public void GenerateNonce_returns_string_of_correct_length()
        {
            var nonce = Client.GenerateNonce();
            Assert.Equal(22, nonce.Length);
        }

        [Fact]
        public void Step1AuthorizationHeader_returns_header()
        {
            var expected = "SibAuth realm=\"RoboForm Online Server\",data=\"biwsbj1sYXN0cGFzcy" +
                           "5ydWJ5QGdtYWlsLmNvbSxyPS1EZUhSclpqQzhEWl8wZThSR3Npc2c=\"";
            var header = Client.Step1AuthorizationHeader(TestData.Credentials);

            Assert.Equal(expected, header);
        }

        [Fact]
        public void Step2AuthorizationHeader_returns_header()
        {
            var expected = "SibAuth sid=\"6Ag93Y02vihucO9IQl1fbg\",data=\"Yz1iaXdzLHI9LURlSFJy" +
                           "WmpDOERaXzBlOFJHc2lzZ00yLXRqZ2YtNjBtLS1GQmhMUTI2dGcscD1lWk5RUE9zOH" +
                           "FIRi9nSGVSWXEyekhmZ0gxNmdJS05xdGFPak5rUjlrRTRrPQ==\"";
            var header = Client.Step2AuthorizationHeader(TestData.Credentials, TestData.AuthInfo);

            Assert.Equal(expected, header);
        }

        //
        // Helpers
        //

        private static void AsserSessionsAretEqual(Session a, Session b)
        {
            Assert.Equal(b.Token, a.Token);
            Assert.Equal(b.DeviceId, a.DeviceId);
            Assert.Equal(b.Cookies, a.Cookies);
        }

        //
        // Data
        //

        private const string Step1Header = "WWW-Authenticate-step1";
        private static readonly Dictionary<string, string> Step2Cookies = new Dictionary<string, string>
        {
            ["sib-auth"] = "AQAUABAAdN_MjkCW",
            ["sib-deviceid"] = "B972fc9818e7",
        };

        private static readonly Session Session = new Session("AQAUABAAdN_MjkCW", "B972fc9818e7");
    }
}