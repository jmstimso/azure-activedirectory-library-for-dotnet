﻿//------------------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Identity.Core;
using Microsoft.Identity.Core.UI;
using Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.Platform;
using NSubstitute;
using Microsoft.Identity.Core.Helpers;

namespace Test.ADAL.NET.Common.Mocks
{
    internal static class MockHelpers
    {
        public static MockWebUI ConfigureMockWebUI(AuthorizationResult authorizationResult)
        {
            return ConfigureMockWebUI(authorizationResult, new Dictionary<string, string>());
        }

        public static MockWebUI ConfigureMockWebUI(
            AuthorizationResult authorizationResult,
            IDictionary<string, string> queryParamsToValidate,
            bool addStateToAuthroizationResult = true)
        {
            MockWebUI webUi = new MockWebUI(addStateToAuthroizationResult);
            webUi.QueryParamsToValidate = queryParamsToValidate;
            webUi.MockResult = authorizationResult;

            IWebUIFactory mockFactory = Substitute.For<IWebUIFactory>();
            mockFactory.CreateAuthenticationDialog(Arg.Any<CoreUIParent>(), Arg.Any<RequestContext>()).Returns(webUi);
            WebUIFactoryProvider.WebUIFactory = mockFactory;

            return webUi;
        }

        public static IDictionary<string, string> GetDefaultAuthorizationRequestParams()
        {
            return new Dictionary<string, string>()
            {
                ["resource"] = AdalTestConstants.DefaultResource,
                ["client_id"] = AdalTestConstants.DefaultClientId,
                ["response_type"] = "code",
                ["redirect_uri"] = AdalTestConstants.DefaultRedirectUri.ToString(),
                ["code_challenge_method"] = "S256",
                ["response_mode"] = "form_post",
                ["x-client-sku"] = "PCL.Desktop"
            };
        }

        public static Stream GenerateStreamFromString(string s)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        public static MockHttpMessageHandler CreateInstanceDiscoveryMockHandler(string url)
        {
            return CreateInstanceDiscoveryMockHandler(url,
                @"{
                        ""tenant_discovery_endpoint"":""https://login.microsoftonline.com/tenant/.well-known/openid-configuration"",
                        ""api-version"":""1.1"",
                        ""metadata"":[
                            {
                            ""preferred_network"":""login.microsoftonline.com"",
                            ""preferred_cache"":""login.windows.net"",
                            ""aliases"":[
                                ""login.microsoftonline.com"",
                                ""login.windows.net"",
                                ""login.microsoft.com"",
                                ""sts.windows.net""]},
                            {
                            ""preferred_network"":""login.partner.microsoftonline.cn"",
                            ""preferred_cache"":""login.partner.microsoftonline.cn"",
                            ""aliases"":[
                                ""login.partner.microsoftonline.cn"",
                                ""login.chinacloudapi.cn""]},
                            {
                            ""preferred_network"":""login.microsoftonline.de"",
                            ""preferred_cache"":""login.microsoftonline.de"",
                            ""aliases"":[
                                    ""login.microsoftonline.de""]},
                            {
                            ""preferred_network"":""login.microsoftonline.us"",
                            ""preferred_cache"":""login.microsoftonline.us"",
                            ""aliases"":[
                                ""login.microsoftonline.us"",
                                ""login.usgovcloudapi.net""]},
                            {
                            ""preferred_network"":""login-us.microsoftonline.com"",
                            ""preferred_cache"":""login-us.microsoftonline.com"",
                            ""aliases"":[
                                ""login-us.microsoftonline.com""]}
                        ]
                }");
        }

        public static MockHttpMessageHandler CreateInstanceDiscoveryMockHandler(string url, string content)
        {
            return new MockHttpMessageHandler(url)
            {
                //Url = "",
                Method = HttpMethod.Get,
                ResponseMessage = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                }
            };
        }

        public static HttpResponseMessage CreateSuccessTokenResponseMessage()
        {
            return CreateSuccessTokenResponseMessage(false);
        }

        public static HttpResponseMessage CreateSuccessTokenResponseMessage(bool setExtendedExpiresIn)
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.OK);
            string extendedExpiresIn = "";

            if (setExtendedExpiresIn)
            {
                extendedExpiresIn = "\"ext_expires_in\":\"7200\",";
            }

            var clientInfo = new ClientInfo
            {
                UniqueObjectIdentifier = AdalTestConstants.DefaultUniqueIdentifier,
                UniqueTenantIdentifier = AdalTestConstants.DefaultUniqueTenantIdentifier
            };
            var base64EncodedSerializedClientInfo = Base64UrlHelpers.Encode(JsonHelper.SerializeToJson<ClientInfo>(clientInfo));


            HttpContent content = new StringContent("{\"token_type\":\"Bearer\",\"expires_in\":\"3600\"," + extendedExpiresIn + "\"resource\":\"resource1\",\"access_token\":\"some-access-token\"," +
                                                    "\"refresh_token\":\"" + AdalTestConstants.DefaultRefreshTokenValue + "\",\"id_token\":\"" +
                                                    CreateAdalIdToken(AdalTestConstants.DefaultUniqueId, AdalTestConstants.DefaultDisplayableId) + "\"," +
                                  "\"client_info\":\"" + base64EncodedSerializedClientInfo + "\"}");

            responseMessage.Content = content;
            return responseMessage;
        }

        public static HttpResponseMessage CreateSuccessDeviceCodeResponseMessage(int expirationTimeInSeconds = 900, int retryInternvalInSeconds = 5)
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.OK);

            HttpContent content = new StringContent(
                "{\"user_code\":\"some-user-code\",\"device_code\":\"some-device-code\",\"verification_url\":\"some-URL\"," +
                "\"expires_in\":\"" + expirationTimeInSeconds.ToString(CultureInfo.InvariantCulture) + "\"," +
                "\"interval\":\"" + retryInternvalInSeconds.ToString(CultureInfo.InvariantCulture) + "\"," +
                "\"message\":\"some-message\"}");
            responseMessage.Content = content;
            return responseMessage;
        }

        public static HttpResponseMessage CreateInvalidRequestTokenResponseMessage()
        {
            return
                CreateFailureResponseMessage(
                    "{\"error\":\"invalid_request\",\"error_description\":\"AADSTS70002: Some error message. Trace ID: f7ec686c-9196-4220-a754-cd9197de44e9 Correlation ID: 04bb0cae-580b-49ac-9a10-b6c3316b1eaa Timestamp: 2015-09-16 07:24:55Z\",\"error_codes\":[70002,70008],\"timestamp\":\"2015-09-16 07:24:55Z\",\"trace_id\":\"f7ec686c-9196-4220-a754-cd9197de44e9\",\"correlation_id\":\"04bb0cae-580b-49ac-9a10-b6c3316b1eaa\"}");
        }

        public static HttpResponseMessage CreateInvalidGrantTokenResponseMessage()
        {
            return
                CreateFailureResponseMessage(
                    "{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS70002: Error validating credentials.AADSTS70008: The provided access grant is expired or revoked.Trace ID: f7ec686c-9196-4220-a754-cd9197de44e9Correlation ID: 04bb0cae-580b-49ac-9a10-b6c3316b1eaaTimestamp: 2015-09-16 07:24:55Z\",\"error_codes\":[70002,70008],\"timestamp\":\"2015-09-16 07:24:55Z\",\"trace_id\":\"f7ec686c-9196-4220-a754-cd9197de44e9\",\"correlation_id\":\"04bb0cae-580b-49ac-9a10-b6c3316b1eaa\"}");
        }

        public static HttpResponseMessage CreateHttpErrorResponse()
        {
            return
                CreateFailureResponseMessage(
                    "{\"ErrorSubCode\":\"70323\",\"error\":\"invalid_grant\",\"error_description\":\"AADSTS70002: Error validating credentials.AADSTS70008: The provided access grant is expired or revoked.Trace ID: f7ec686c-9196-4220-a754-cd9197de44e9Correlation ID: 04bb0cae-580b-49ac-9a10-b6c3316b1eaaTimestamp: 2015-09-16 07:24:55Z\",\"error_codes\":[70002,70008],\"timestamp\":\"2015-09-16 07:24:55Z\",\"trace_id\":\"f7ec686c-9196-4220-a754-cd9197de44e9\",\"correlation_id\":\"04bb0cae-580b-49ac-9a10-b6c3316b1eaa\"}");
        }

        public static HttpResponseMessage CreateDeviceCodeErrorResponse()
        {
            return
                CreateFailureResponseMessage("{\"error\":\"invalid_request\",\"error_description\":\"AADSTS90014: some error message.\\r\\nTrace ID: 290d2ab9-40f2-4716-92e2-4a72fc480000\\r\\nCorrelation ID: 2eee49ee-620e-42c2-9a3c-dcf81955b20f\\r\\nTimestamp: 2017-09-20 23:05:56Z\",\"error_codes\":[90014],\"timestamp\":\"2017-09-20 23:05:56Z\",\"trace_id\":\"290d2ab9-40f2-4716-92e2-4a72fc480000\",\"correlation_id\":\"2eee49ee-620e-42c2-9a3c-dcf81955b20f\"}");
        }

        public static HttpResponseMessage CreateDeviceCodeExpirationErrorResponse()
        {
            return
                CreateFailureResponseMessage("{\"error\":\"code_expired\",\"error_description\":\"AADSTS70019: Verification code expired.\\r\\nTrace ID: c16f4b65-c002-493a-b7cc-a33f3fe70000\\r\\nCorrelation ID: 91e8e5de-8974-4899-beec-08f7654fa1fd\\r\\nTimestamp: 2017-09-22 19:39:55Z\",\"error_codes\":[70019],\"timestamp\":\"2017-09-22 19:39:55Z\",\"trace_id\":\"c16f4b65-c002-493a-b7cc-a33f3fe70000\",\"correlation_id\":\"91e8e5de-8974-4899-beec-08f7654fa1fd\"}");
        }

        public static HttpResponseMessage CreateFailureResponseMessage(string message)
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest);
            HttpContent content = new StringContent(message);
            responseMessage.Content = content;
            return responseMessage;
        }

        public static HttpResponseMessage CreateCustomHeaderFailureResponseMessage(IEnumerable<KeyValuePair<string, string>> headers)
        {
            HttpResponseMessage responseMessage = CreateHttpErrorResponse();

            foreach (KeyValuePair<string, string> header in headers)
            {
                responseMessage.Headers.Add(header.Key, header.Value);
            }

            return responseMessage;
        }

        public static HttpResponseMessage CreateResiliencyMessage(HttpStatusCode statusCode)
        {
            HttpResponseMessage responseMessage = null;
            HttpContent content = null;

            responseMessage = new HttpResponseMessage(statusCode);
            content = new StringContent("Server Error 500-599");

            if (responseMessage != null)
            {
                responseMessage.Content = content;
            }
            return responseMessage;
        }

        public static HttpResponseMessage CreateSuccessfulClientCredentialTokenResponseMessage()
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.OK);
            HttpContent content =
                new StringContent(
                    "{\"token_type\":\"Bearer\",\"expires_in\":\"3599\",\"access_token\":\"header.payload.signature\"}");
            responseMessage.Content = content;
            return responseMessage;
        }

        public static HttpResponseMessage CreateSuccessTokenResponseMessage(string uniqueId, string displayableId, string resource)
        {
            string idToken = string.Format(CultureInfo.InvariantCulture, "{0}", CreateAdalIdToken(uniqueId, displayableId));
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.OK);

            var clientInfo = new ClientInfo
            {
                UniqueObjectIdentifier = displayableId,
                UniqueTenantIdentifier = AdalTestConstants.SomeTenantId
            };
            var base64EncodedSerializedClientInfo = Base64UrlHelpers.Encode(JsonHelper.SerializeToJson<ClientInfo>(clientInfo));

            HttpContent content =
                new StringContent("{\"token_type\":\"Bearer\",\"expires_in\":\"3599\",\"resource\":\"" +
                                  resource +
                                  "\",\"access_token\":\"some-access-token\",\"refresh_token\":\"OAAsomethingencryptedQwgAA\",\"id_token\":\"" +
                                  idToken +
                                  "\"," + "\"client_info\":\"" + base64EncodedSerializedClientInfo + "\"}");
            responseMessage.Content = content;
            return responseMessage;
        }

        public static string CreateMsalIdToken(string uniqueId, string displayableId)
        {
            return CreateMsalIdToken(uniqueId, displayableId, CoreTestConstants.Utid);
        }

        public static string CreateMsalIdToken(string uniqueId, string displayableId, string tenantId)
        {
            string id = "{\"aud\": \"e854a4a7-6c34-449c-b237-fc7a28093d84\"," +
                        "\"iss\": \"https://login.microsoftonline.com/6c3d51dd-f0e5-4959-b4ea-a80c4e36fe5e/v2.0/\"," +
                        "\"iat\": 1455833828," +
                        "\"nbf\": 1455833828," +
                        "\"exp\": 1455837728," +
                        "\"ipaddr\": \"131.107.159.117\"," +
                        "\"name\": \"Marrrrrio Bossy\"," +
                        "\"oid\": \"" + uniqueId + "\"," +
                        "\"preferred_username\": \"" + displayableId + "\"," +
                        "\"sub\": \"K4_SGGxKqW1SxUAmhg6C1F6VPiFzcx-Qd80ehIEdFus\"," +
                        "\"tid\": \"" + tenantId + "\"," +
                        "\"ver\": \"2.0\"}";
            return string.Format(CultureInfo.InvariantCulture, "someheader.{0}.somesignature", Base64UrlHelpers.Encode(id));
        }

        internal static string CreateAdalIdToken(string uniqueId, string displayableId)
        {
            string header = "{alg: \"none\"," +
                            "typ:\"JWT\"" +
                            "}";
            string payload = "{\"aud\": \"e854a4a7-6c34-449c-b237-fc7a28093d84\"," +
                             "\"iss\": \"https://login.microsoftonline.com/6c3d51dd-f0e5-4959-b4ea-a80c4e36fe5e/\"," +
                             "\"iat\": 1455833828," +
                             "\"nbf\": 1455833828," +
                             "\"exp\": 1455837728," +
                             "\"ipaddr\": \"131.107.159.117\"," +
                             "\"name\": \"Mario Rossi\"," +
                             "\"oid\": \"" + uniqueId + "\"," +
                             "\"upn\": \"" + displayableId + "\"," +
                             "\"sub\": \"werwerewrewrew-Qd80ehIEdFus\"," +
                             "\"tid\": \"" + AdalTestConstants.SomeTenantId + "\"," +
                             "\"ver\": \"2.0\"}";

            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.signature", Base64UrlHelpers.Encode(header), Base64UrlHelpers.Encode(payload));
        }


        public static string CreateClientInfo(string uid, string utid)
        {
            return Base64UrlHelpers.Encode("{\"uid\":\"" + uid + "\",\"utid\":\"" + utid + "\"}");
        }

        public static HttpResponseMessage CreateRequestTimeoutResponseMessage()
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.RequestTimeout);
            HttpContent content = new StringContent("Request Timed Out.");
            responseMessage.Content = content;
            return responseMessage;
        }

        public static HttpResponseMessage CreateOpenIdConfigurationResponse(string authority, string qp = "")
        {
            var authorityUri = new Uri(authority);
            string path = authorityUri.AbsolutePath.Substring(1);
            string tenant = path.Substring(0, path.IndexOf("/", StringComparison.Ordinal));
            if (tenant.ToLowerInvariant().Equals("common", StringComparison.OrdinalIgnoreCase))
            {
                tenant = "{tenant}";
            }

            if (!string.IsNullOrEmpty(qp))
            {
                qp = "?" + qp;
            }

            return CreateSuccessResponseMessage(string.Format(CultureInfo.InvariantCulture,
                                                              "{{\"authorization_endpoint\":\"{0}oauth2/v2.0/authorize{2}\",\"token_endpoint\":\"{0}oauth2/v2.0/token{2}\",\"issuer\":\"https://sts.windows.net/{1}\"}}",
                                                              authority, tenant, qp));
        }

        public static HttpResponseMessage CreateSuccessResponseMessage(string successResponse)
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.OK);
            HttpContent content =
                new StringContent(successResponse);
            responseMessage.Content = content;
            return responseMessage;
        }

        public static void Merge<TKey, TValue>(this Dictionary<TKey, TValue> me, Dictionary<TKey, TValue> merge)
        {
            foreach (var item in merge)
            {
                me[item.Key] = item.Value;
            }
        }
    }
}