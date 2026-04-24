using System.Security.Cryptography;
using System.Text;

namespace GameServer.Api.Services;

public static class PurchaseTokenHasher
{
    public static string Hash(string purchaseToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}
