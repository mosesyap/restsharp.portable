﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;

using RestSharp.Portable.Authenticators;
using RestSharp.Portable.HttpClient;
using RestSharp.Portable.HttpClient.Impl;
using RestSharp.Portable.OAuth1;
using RestSharp.Portable.OAuth1.SignatureProviders;
using RestSharp.Portable.Tests.HttpBin;
using RestSharp.Portable.WebRequest.Impl;

using Xunit;

namespace RestSharp.Portable.Tests
{
    public class AuthenticationTests : RestSharpTests
    {
        [Theory]
        [InlineData(typeof(DefaultHttpClientFactory))]
        [InlineData(typeof(WebRequestHttpClientFactory))]
        public async Task TestHttpBasicAuth(Type factoryType)
        {
            const string username = "user name";
            const string password = "passwd";

            using (var client = new RestClient("http://httpbin.org")
            {
                CookieContainer = new CookieContainer(),
                HttpClientFactory = CreateClientFactory(factoryType, false),
                Credentials = new NetworkCredential(username, password),
                Authenticator = new HttpBasicAuthenticator(),
            })
            {
                var request = new RestRequest("basic-auth/{username}/{password}", Method.GET);
                request.AddUrlSegment("username", username);
                request.AddUrlSegment("password", password);
                var response = await client.Execute<AuthenticationResult>(request).ConfigureAwait(false);

                Assert.True(response.Data.Authenticated);
                Assert.Equal(username, response.Data.User);
            }
        }

        [Theory]
        [InlineData(typeof(DefaultHttpClientFactory))]
        [InlineData(typeof(WebRequestHttpClientFactory))]
        public async Task TestHttpBasicAuthHidden(Type factoryType)
        {
            const string username = "user name";
            const string password = "passwd";

            using (var client = new RestClient("http://httpbin.org")
            {
                CookieContainer = new CookieContainer(),
                HttpClientFactory = CreateClientFactory(factoryType, false),
                Credentials = new NetworkCredential(username, password),
                Authenticator = new HttpHiddenBasicAuthenticator(),
            })
            {
                var request = new RestRequest("hidden-basic-auth/{username}/{password}", Method.GET);
                request.AddUrlSegment("username", username);
                request.AddUrlSegment("password", password);
                var response = await client.Execute<AuthenticationResult>(request).ConfigureAwait(false);

                Assert.True(response.Data.Authenticated);
                Assert.Equal(username, response.Data.User);
            }
        }

        [Theory]
        [InlineData(typeof(DefaultHttpClientFactory))]
        [InlineData(typeof(WebRequestHttpClientFactory))]
        public async Task TestHttpDigestAuth(Type factoryType)
        {
            const string username = "user name";
            const string password = "passwd";

            using (var client = new RestClient("http://httpbin.org")
            {
                CookieContainer = new CookieContainer(),
                HttpClientFactory = CreateClientFactory(factoryType, false),
                Credentials = new NetworkCredential(username, password),
                Authenticator = new HttpDigestAuthenticator()
            })
            {
                var request = new RestRequest("digest-auth/auth/{username}/{password}", Method.GET);
                request.AddUrlSegment("username", username);
                request.AddUrlSegment("password", password);
                var response = await client.Execute<AuthenticationResult>(request).ConfigureAwait(false);

                Assert.True(response.Data.Authenticated);
                Assert.Equal(username, response.Data.User);
            }
        }

        [Theory]
        [InlineData(typeof(DefaultHttpClientFactory))]
        [InlineData(typeof(WebRequestHttpClientFactory))]
        public async Task TestHttpDigestAuthInt(Type factoryType)
        {
            const string username = "user name";
            const string password = "passwd";

            using (var client = new RestClient("http://httpbin.org/digest-auth")
            {
                CookieContainer = new CookieContainer(),
                HttpClientFactory = CreateClientFactory(factoryType, false),
                Credentials = new NetworkCredential(username, password),
                Authenticator = new HttpDigestAuthenticator()
            })
            {
                // httpbin.org only supports GET for digest-auth...
                var request = new RestRequest("auth-int/{username}/{password}", Method.GET);
                request.AddUrlSegment("username", username);
                request.AddUrlSegment("password", password);
                ////request.AddOrUpdateParameter("param1", "val1");
                var response = await client.Execute<AuthenticationResult>(request).ConfigureAwait(false);

                Assert.True(response.Data.Authenticated);
                Assert.Equal(username, response.Data.User);

                ////Assert.NotNull(response.Data.Form);
                ////Assert.Equal(1, response.Data.Form.Count);
                ////Assert.Contains("param1", response.Data.Form.Keys);
                ////Assert.Equal("val1", response.Data.Form["param1"]);
            }
        }

        [Theory]
        [InlineData(typeof(DefaultHttpClientFactory))]
        [InlineData(typeof(WebRequestHttpClientFactory))]
        public async Task TestOAuth10_RsaSha1(Type factoryType)
        {
            ISignatureProvider signatureProvider;
            var asm = typeof(AuthenticationTests).Assembly;
            var keyResourceName = asm.GetManifestResourceNames().Single(x => x.EndsWith(".OAuthBin.test-rsa.key"));

            using (var stream = asm.GetManifestResourceStream(keyResourceName))
            {
                Assert.NotNull(stream);
                var keyPair = (RsaPrivateCrtKeyParameters) new PemReader(new StreamReader(stream)).ReadObject();
                var rsa = Org.BouncyCastle.Security.DotNetUtilities.ToRSA(keyPair);
                signatureProvider = new RsaSha1SignatureProvider(rsa);
            }

            await TestOAuth10(CreateClientFactory(factoryType, false), signatureProvider).ConfigureAwait(false);
        }

        [Theory]
        [InlineData(typeof(DefaultHttpClientFactory))]
        [InlineData(typeof(WebRequestHttpClientFactory))]
        public async Task TestOAuth10_HmacSha1(Type factoryType)
        {
            await TestOAuth10(CreateClientFactory(factoryType, false), new HmacSha1SignatureProvider()).ConfigureAwait(false);
        }

        [Theory]
        [InlineData(typeof(DefaultHttpClientFactory))]
        [InlineData(typeof(WebRequestHttpClientFactory))]
        public async Task TestOAuth10_PlainText(Type factoryType)
        {
            await TestOAuth10(CreateClientFactory(factoryType, false), new PlainTextSignatureProvider()).ConfigureAwait(false);
        }

        private async Task TestOAuth10(IHttpClientFactory httpClientFactory, ISignatureProvider signatureProvider)
        {
            using (var client = new RestClient("http://oauthbin.com/v1/")
            {
                HttpClientFactory = httpClientFactory,
            })
            {
                var consumerKey = "key";
                var consumerSecret = "secret";

                var authenticator = OAuth1Authenticator.ForRequestToken(consumerKey, consumerSecret, "http://localhost/test");
                authenticator.SignatureProvider = signatureProvider;
                client.Authenticator = authenticator;

                string requestToken, requestTokenSecret;

                {
                    var request = new RestRequest("request-token");
                    var response = await client.Execute(request).ConfigureAwait(false);
                    var requestTokenResponse = Encoding.UTF8.GetString(response.RawBytes);
                    Assert.DoesNotContain('\n', requestTokenResponse);

                    var tokenInfo = (from part in requestTokenResponse.Split('&')
                                     let equalSignPos = part.IndexOf('=')
                                     let partKey = part.Substring(0, equalSignPos)
                                     let partValue = part.Substring(equalSignPos + 1)
                                     select new
                                     {
                                         partKey,
                                         partValue
                                     }).ToDictionary(x => x.partKey, x => x.partValue);

                    Assert.Contains("oauth_token", tokenInfo.Keys);
                    Assert.Contains("oauth_token_secret", tokenInfo.Keys);

                    requestToken = tokenInfo["oauth_token"];
                    requestTokenSecret = tokenInfo["oauth_token_secret"];
                }

                authenticator = OAuth1Authenticator.ForAccessToken(consumerKey, consumerSecret, requestToken, requestTokenSecret);
                authenticator.SignatureProvider = signatureProvider;
                client.Authenticator = authenticator;

                string accessKey, accessSecret;

                {
                    var request = new RestRequest("access-token");
                    var response = await client.Execute(request).ConfigureAwait(false);
                    var accessTokenResponse = Encoding.UTF8.GetString(response.RawBytes);
                    Assert.DoesNotContain('\n', accessTokenResponse);

                    var tokenInfo = (from part in accessTokenResponse.Split('&')
                                     let equalSignPos = part.IndexOf('=')
                                     let partKey = part.Substring(0, equalSignPos)
                                     let partValue = part.Substring(equalSignPos + 1)
                                     select new
                                     {
                                         partKey,
                                         partValue
                                     }).ToDictionary(x => x.partKey, x => x.partValue);

                    Assert.Contains("oauth_token", tokenInfo.Keys);
                    Assert.Contains("oauth_token_secret", tokenInfo.Keys);

                    accessKey = tokenInfo["oauth_token"];
                    accessSecret = tokenInfo["oauth_token_secret"];
                }

                authenticator = OAuth1Authenticator.ForProtectedResource(consumerKey, consumerSecret, accessKey, accessSecret);
                authenticator.SignatureProvider = signatureProvider;
                client.Authenticator = authenticator;

                {
                    var request = new RestRequest("echo", Method.POST);
                    request.AddOrUpdateParameter("one", "1");
                    request.AddOrUpdateParameter("two", "2");
                    var response = await client.Execute(request).ConfigureAwait(false);
                    var text = Encoding.UTF8.GetString(response.RawBytes);
                    Assert.DoesNotContain('\n', text);

                    var data = (from part in text.Split('&')
                                let equalSignPos = part.IndexOf('=')
                                let partKey = part.Substring(0, equalSignPos)
                                let partValue = part.Substring(equalSignPos + 1)
                                select new
                                {
                                    partKey,
                                    partValue
                                }).ToDictionary(x => x.partKey, x => x.partValue);
                    Assert.Contains("one", data.Keys);
                    Assert.Contains("two", data.Keys);
                    Assert.Equal("1", data["one"]);
                    Assert.Equal("2", data["two"]);
                }
            }
        }
    }
}
