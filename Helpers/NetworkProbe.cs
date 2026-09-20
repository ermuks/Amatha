using System.Net.Http;
using System.Net.NetworkInformation;

namespace Amaranth10API.Helpers;

internal static class NetworkProbe
{
    private static readonly HttpClient Http = CreateClient();

    public static bool HasAdapter => NetworkInterface.GetIsNetworkAvailable();

    public static async Task<bool> CanReachAsync(CancellationToken cancellationToken = default)
    {
        if (!HasAdapter)
        {
            return false;
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "https://erp.teia.co.kr/");
            using HttpResponseMessage response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
    }
}
