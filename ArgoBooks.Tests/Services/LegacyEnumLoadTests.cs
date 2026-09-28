using System.Text.Json;
using ArgoBooks.Core.Enums;
using ArgoBooks.Core.Models.Transactions;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Company files are read with FileService.JsonOptions. A converter in the options list outranks the
/// [JsonConverter] attribute on an enum, so the lenient converters only run when they are listed
/// ahead of JsonStringEnumConverter; otherwise a blank or unknown value stops the file opening.
/// </summary>
public class LegacyEnumLoadTests
{
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"settled\"")]
    [InlineData("null")]
    [InlineData("99")]
    public void UnknownPaymentStatus_LoadsAsPaid(string json)
    {
        var revenue = JsonSerializer.Deserialize<Revenue>($"{{\"paymentStatus\":{json}}}", FileService.JsonOptions)!;
        Assert.Equal(RevenuePaymentStatus.Paid, revenue.PaymentStatus);
    }

    [Theory]
    [InlineData("\"unpaid\"", RevenuePaymentStatus.Unpaid)]
    [InlineData("\"Complete\"", RevenuePaymentStatus.Complete)]
    [InlineData("4", RevenuePaymentStatus.Unpaid)]
    public void KnownPaymentStatus_LoadsAsWritten(string json, RevenuePaymentStatus expected)
    {
        var revenue = JsonSerializer.Deserialize<Revenue>($"{{\"paymentStatus\":{json}}}", FileService.JsonOptions)!;
        Assert.Equal(expected, revenue.PaymentStatus);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"portal\"")]
    public void UnknownPaymentSource_LoadsAsManual(string json)
    {
        var payment = JsonSerializer.Deserialize<Payment>($"{{\"source\":{json}}}", FileService.JsonOptions)!;
        Assert.Equal(PaymentSource.Manual, payment.Source);
    }

    [Fact]
    public void PaymentStatus_StillWritesItsName()
    {
        var json = JsonSerializer.Serialize(new Revenue { PaymentStatus = RevenuePaymentStatus.Unpaid }, FileService.JsonOptions);
        Assert.Contains("\"paymentStatus\": \"Unpaid\"", json);
    }
}
