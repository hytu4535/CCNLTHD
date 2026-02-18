using System;
using System.Security.Cryptography;
using System.Text;

namespace StripeWebhooks.Tests.Utils;

public static class StripeTestSignatures
{
    public static string CreateStripeSignatureHeader(string payload, string endpointSecret, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{ts}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(endpointSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var sig = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        // Stripe-Signature header format
        return $"t={ts},v1={sig}";
    }
}