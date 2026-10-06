using System.Net;
using System.Text.RegularExpressions;
using Amaranth10API.Models;

namespace Amaranth10API.Helpers;

public static class ApplicationDocumentHtml
{
    public static string Create(BusinessTripDocument document)
    {
        string title = WebUtility.HtmlEncode(document.Title);
        string number = WebUtility.HtmlEncode(document.DocumentNumber);
        string status = WebUtility.HtmlEncode(document.DocumentStatus);
        string body = document.DocContents;
        if (string.IsNullOrWhiteSpace(body))
        {
            body = "<pre>" + WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(document.ContentsWord)
                ? "신청서 본문이 없습니다. ERP에서 문서를 확인해 주세요."
                : document.ContentsWord) + "</pre>";
        }

        // 문서가 완전한 HTML이어도 본문과 양식 스타일을 그대로 표시합니다.
        Match htmlBody = Regex.Match(body, @"<body\b[^>]*>(?<body>.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (htmlBody.Success)
        {
            string styles = string.Concat(Regex.Matches(body, @"<style\b[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Cast<Match>().Select(match => match.Value));
            body = styles + htmlBody.Groups["body"].Value;
        }

        return "<!doctype html><html lang='ko'><head><meta charset='utf-8'>" +
            "<meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'unsafe-inline' https://erp.teia.co.kr; img-src data: https://erp.teia.co.kr; font-src data: https://erp.teia.co.kr; form-action 'none'; base-uri https://erp.teia.co.kr\">" +
            "<base href='https://erp.teia.co.kr/'><title>" + title + "</title>" +
            "<style>body{margin:24px;background:#f3f5f8;color:#2a3548;font-family:'Malgun Gothic',sans-serif}header{margin:0 0 18px}h1{font-size:18px;margin:0 0 8px}header p{font-size:13px;color:#5a6f8a;margin:0}.document{background:white;padding:24px;overflow:auto}pre{white-space:pre-wrap;font-family:inherit}input,select,textarea,button{pointer-events:none}</style></head><body>" +
            "<header><h1>" + title + "</h1><p>" + number + " " + status + " · 신청서 원문</p></header>" +
            "<div class='document'>" + body + "</div></body></html>";
    }
}
