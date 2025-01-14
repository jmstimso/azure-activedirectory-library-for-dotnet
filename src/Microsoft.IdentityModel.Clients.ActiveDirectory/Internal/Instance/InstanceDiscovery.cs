﻿//----------------------------------------------------------------------
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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System;
using Microsoft.Identity.Core;
using Microsoft.Identity.Core.OAuth2;
using Microsoft.Identity.Core.Http;

namespace Microsoft.IdentityModel.Clients.ActiveDirectory
{
    [DataContract]
    internal sealed class InstanceDiscoveryResponse
    {
        [DataMember(Name = "tenant_discovery_endpoint")]
        public string TenantDiscoveryEndpoint { get; set; }

        [DataMember(Name = "metadata")]
        public InstanceDiscoveryMetadataEntry[] Metadata { get; set; }
    }

    [DataContract]
    internal sealed class InstanceDiscoveryMetadataEntry
    {
        [DataMember(Name = "preferred_network")]
        public string PreferredNetwork { get; set; }

        [DataMember(Name = "preferred_cache")]
        public string PreferredCache { get; set; }

        [DataMember(Name = "aliases")]
        public string[] Aliases { get; set; }
    }

    internal class InstanceDiscovery
    {
        public const string DefaultTrustedAuthority = "login.microsoftonline.com";

        private static HashSet<string> WhitelistedAuthorities = new HashSet<string>(new[]
        {
            "login.windows.net", // Microsoft Azure Worldwide - Used in validation scenarios where host is not this list 
            "login.chinacloudapi.cn", // Microsoft Azure China
            "login.microsoftonline.de", // Microsoft Azure Blackforest
            "login-us.microsoftonline.com", // Microsoft Azure US Government - Legacy
            "login.microsoftonline.us", // Microsoft Azure US Government
            "login.microsoftonline.com" // Microsoft Azure Worldwide
        });

        private static HashSet<string> WhitelistedDomains = new HashSet<string>();

        private readonly IHttpManager _httpManager;

        public InstanceDiscovery(IHttpManager httpManager)
        {
            _httpManager = httpManager;
        }

        internal static bool IsWhitelisted(string authorityHost)
        {
            return WhitelistedAuthorities.Contains(authorityHost) || 
                WhitelistedDomains.Any(domain => authorityHost.EndsWith(domain, StringComparison.OrdinalIgnoreCase));
        }

        // The following cache could be private, but we keep it public so that internal unit test can take a peek into it.
        // Keys are host strings.
        public static ConcurrentDictionary<string, InstanceDiscoveryMetadataEntry> InstanceCache { get; } = 
            new ConcurrentDictionary<string, InstanceDiscoveryMetadataEntry>();

        private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public async Task<InstanceDiscoveryMetadataEntry> GetMetadataEntryAsync(Uri authority, bool validateAuthority,
            RequestContext requestContext)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            InstanceDiscoveryMetadataEntry entry = null;
            if (!InstanceCache.TryGetValue(authority.Host, out entry))
            {
                await semaphore.WaitAsync().ConfigureAwait(false); // SemaphoreSlim.WaitAsync() will not block current thread
                try
                {
                    // Dynamicly add dSTS endpoints to whitelist
                    if (authority.Host.Contains(".dsts.") && 
                        !WhitelistedDomains.Any(domain => authority.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase)))
                    {
                        int dstsSuffixIndex = authority.Host.IndexOf(".dsts.", StringComparison.OrdinalIgnoreCase) + 1;
                        WhitelistedDomains.Add(authority.Host.Substring(dstsSuffixIndex));
                    }

                    if (!InstanceCache.TryGetValue(authority.Host, out entry))
                    {
                        await DiscoverAsync(authority, validateAuthority, requestContext).ConfigureAwait(false);
                        InstanceCache.TryGetValue(authority.Host, out entry);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }

            return entry;
        }

        public string FormatAuthorizeEndpoint(string host, string tenant)
        {
            return string.Format(CultureInfo.InvariantCulture, "https://{0}/{1}/oauth2/authorize", host, tenant);
        }

        private static string GetTenant(Uri uri)
        {
            return uri.Segments[uri.Segments.Length - 1].TrimEnd('/');
        }

        private static string GetHost(Uri uri)
        {
            if (WhitelistedDomains.Any(domain => uri.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase)))
            {
                // Host + Virtual directory
                return string.Format(CultureInfo.InvariantCulture, "{0}/{1}", uri.Host, uri.Segments[1].TrimEnd('/'));
            }
            else
            {
                return uri.Host;
            }
        }

        // No return value. Modifies InstanceCache directly.
        private async Task DiscoverAsync(Uri authority, bool validateAuthority, RequestContext requestContext)
        {
            string instanceDiscoveryEndpoint = string.Format(
                CultureInfo.InvariantCulture,
                "https://{0}/common/discovery/instance?api-version=1.1&authorization_endpoint={1}",
                IsWhitelisted(authority.Host) ? GetHost(authority) : DefaultTrustedAuthority,
                FormatAuthorizeEndpoint(authority.Host, GetTenant(authority)));

            var client = new OAuthClient(_httpManager, instanceDiscoveryEndpoint, requestContext);

            InstanceDiscoveryResponse discoveryResponse = null;
            try
            {
                discoveryResponse = await client.GetResponseAsync<InstanceDiscoveryResponse>().ConfigureAwait(false);
                if (validateAuthority && discoveryResponse.TenantDiscoveryEndpoint == null)
                {
                    // hard stop here
                    throw new AdalException(AdalError.AuthorityNotInValidList);
                }
            }
            catch (AdalServiceException ex)
            {
                // The pre-existing implementation (https://github.com/AzureAD/azure-activedirectory-library-for-dotnet/pull/796/files#diff-e4febd8f40f03e71bcae0f990f9690eaL99)
                // has been coded in this way: it catches the AdalServiceException and then translate it into 2 validation-relevant exceptions.
                // So the following implementation absorbs these specific exceptions when the validateAuthority flag is false.
                // All other unexpected exceptions will still bubble up, as always.
                if (validateAuthority)
                {
                    // hard stop here
                    throw new AdalException(
                        (ex.ErrorCode == "invalid_instance")
                            ? AdalError.AuthorityNotInValidList
                            : AdalError.AuthorityValidationFailed, ex);
                }
            }

            foreach (var entry in discoveryResponse?.Metadata ?? Enumerable.Empty<InstanceDiscoveryMetadataEntry>())
            {
                foreach (var aliasedAuthority in entry?.Aliases ?? Enumerable.Empty<string>())
                {
                    InstanceCache.TryAdd(aliasedAuthority, entry);
                }
            }

            AddMetadataEntry(authority.Host);
        }

        // To populate a host into the cache as-is, when it is not already there
        public bool AddMetadataEntry(string host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            return InstanceCache.TryAdd(host, new InstanceDiscoveryMetadataEntry
            {
                PreferredNetwork = host,
                PreferredCache = host,
                Aliases = null
            });
        }
    }
}
