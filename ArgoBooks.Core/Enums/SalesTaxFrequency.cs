using System.Text.Json.Serialization;

namespace ArgoBooks.Core.Enums;

/// <summary>How often the business files its sales tax return.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SalesTaxFrequency
{
    Monthly,
    Quarterly,
    Yearly
}
