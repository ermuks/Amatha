using System.Net;
using Amaranth10API.Models;

namespace Amaranth10API.Helpers;

public static class MailBodyHtml
{
    public static string Create(MailBody body)
    {
        if (!string.IsNullOrWhiteSpace(body.Html)) return body.Html;
        string text = string.IsNullOrWhiteSpace(body.Plain) ? "본문이 없는 메일입니다." : body.Plain;
        return "<!doctype html><html><head><meta charset=\"utf-8\"></head><body><pre style=\"white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.7 sans-serif;padding:16px\">"
            + WebUtility.HtmlEncode(text) + "</pre></body></html>";
    }

    public static string ContentPolicy(bool images) =>
        "default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src data:" + (images ? " https:" : "") +
        "; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; connect-src 'none'";

    public static bool IsWebLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
