using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using IPNetwork = System.Net.IPNetwork;

namespace XtreamForge.ApiService.Infrastructure;

public static class ForwardedHeadersExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Configures the forwarded headers middleware: behind a reverse proxy terminating TLS, the scheme, host, and client address
        /// of the original request (used to rewrite <c>server_info</c>) come from the headers sent by the loopback addresses and the
        /// <see cref="ReverseProxyOptions"/> proxies; the headers of any other sender are ignored.
        /// </summary>
        public IServiceCollection AddForwardedHeaders()
        {
            services.AddOptions<ReverseProxyOptions>()
                .BindConfiguration(ReverseProxyOptions.SectionName)
                .Validate(options => options.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _)), $"{ReverseProxyOptions.SectionName}:KnownProxies must contain IP addresses.")
                .Validate(options => options.KnownNetworks.All(network => IPNetwork.TryParse(network, out _)), $"{ReverseProxyOptions.SectionName}:KnownNetworks must contain CIDR networks.")
                .ValidateOnStart();

            services.AddOptions<ForwardedHeadersOptions>()
                .Configure<IOptions<ReverseProxyOptions>>((forwardedHeaders, reverseProxyOptions) =>
                {
                    forwardedHeaders.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

                    foreach (var proxy in reverseProxyOptions.Value.KnownProxies)
                    {
                        forwardedHeaders.KnownProxies.Add(IPAddress.Parse(proxy));
                    }

                    foreach (var network in reverseProxyOptions.Value.KnownNetworks)
                    {
                        forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse(network));
                    }
                });

            return services;
        }
    }
}
