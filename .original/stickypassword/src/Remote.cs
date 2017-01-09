// Copyright (C) 2017 Dmitry Yakimenko (detunized@gmail.com).
// Licensed under the terms of the MIT license. See LICENCE for details.

using System;
using System.Collections.Generic;
using RestSharp;
using RestSharp.Deserializers;

namespace StickyPassword
{
    public static class Remote
    {
        private const string ApiUrl = "https://spcb.stickypassword.com/SPCClient/";

        public static byte[] GetEncryptedToken(string username, string deviceId, DateTime timestamp)
        {
            return GetEncryptedToken(username, deviceId, timestamp, new RestClient());
        }

        public class GetCrpTokenResponse
        {
            public string CrpToken { get; set; }
        }

        public class SpcResponse
        {
            public int Status { get; set; }
            public GetCrpTokenResponse GetCrpTokenResponse { get; set; }
        }

        public static byte[] GetEncryptedToken(string username, string deviceId, DateTime timestamp, IRestClient client)
        {
            ConfigureClient(client, deviceId);
            var response = Post(client, "GetCrpToken", timestamp, new Dictionary<string, string>
            {
                {"uaid", username},
            });

            var parsed = new XmlDeserializer().Deserialize<SpcResponse>(response);
            if (parsed == null || parsed.GetCrpTokenResponse == null)
                throw new InvalidOperationException();

            return parsed.GetCrpTokenResponse.CrpToken.Decode64();
        }

        private static void ConfigureClient(IRestClient client, string deviceId)
        {
            client.BaseUrl = new Uri(ApiUrl);
            client.UserAgent = GetUserAgent(deviceId);
        }

        private static string GetUserAgent(string deviceId)
        {
            return string.Format("SP/8.0.3436 Prot=2 ID={0} Lng=EN Os=Android/4.4.4 Lic= LicStat= PackageID=", deviceId);
        }

        private static IRestResponse Post(IRestClient client, string endPoint, DateTime timestamp,
            Dictionary<string, string> parameters)
        {
            var request = new RestRequest(endPoint, Method.POST);
            SetRequestHeaders(request, timestamp);

            foreach (var i in parameters)
                request.AddParameter(i.Key, i.Value);

            return client.Execute(request);
        }

        private static void SetRequestHeaders(RestRequest request, DateTime timestamp)
        {
            request.AddHeader("Date", timestamp.ToUniversalTime().ToString("R"));
            request.AddHeader("Accept", "application/xml");
        }
    }
}
