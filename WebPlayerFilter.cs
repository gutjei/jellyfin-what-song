using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SongPause;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
        => services.AddSingleton<IStartupFilter, WebPlayerFilter>();
}

internal sealed class WebPlayerFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(AddPlayerPlugin);
        next(app);
    };

    private static async Task AddPlayerPlugin(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        var path = request.Path.Value ?? "";
        var config = path.EndsWith("/web/config.json", StringComparison.OrdinalIgnoreCase);
        var index = path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase);
        if (!HttpMethods.IsGet(request.Method) || !(config || index))
        {
            await next(context);
            return;
        }

        // Ask the static file handler for the complete, uncompressed representation.
        foreach (var header in new[] { "Accept-Encoding", "Range", "If-Range", "If-None-Match", "If-Modified-Since" })
            request.Headers.Remove(header);

        var response = context.Response;
        var output = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            response.Body = output;
        }

        buffer.Position = 0;
        if (response.StatusCode != 200 || response.Headers.ContainsKey("Content-Encoding"))
        {
            await buffer.CopyToAsync(output, context.RequestAborted);
            return;
        }

        using var reader = new StreamReader(buffer, Encoding.UTF8, leaveOpen: true);
        var original = await reader.ReadToEndAsync(context.RequestAborted);
        var updated = config ? AddToConfig(original) : AddToIndex(original);
        var bytes = Encoding.UTF8.GetBytes(updated);
        response.Headers.Remove("ETag");
        response.Headers.Remove("Last-Modified");
        response.Headers.Remove("Accept-Ranges");
        response.Headers.CacheControl = "no-store";
        response.ContentLength = bytes.Length;
        await output.WriteAsync(bytes, context.RequestAborted);
    }

    private static string AddToIndex(string html)
    {
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return head < 0 || html.Contains("../SongPause/client.js", StringComparison.Ordinal)
            ? html
            : html.Insert(head, "<script src=\"../SongPause/client.js\"></script>");
    }

    private static string AddToConfig(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject config && config["plugins"] is JsonArray plugins
                && !plugins.Any(value => value?.ToString() == "SongPause"))
            {
                plugins.Add("SongPause");
                return config.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Preserve the web client's own fallback for malformed configuration.
        }

        return json;
    }
}
