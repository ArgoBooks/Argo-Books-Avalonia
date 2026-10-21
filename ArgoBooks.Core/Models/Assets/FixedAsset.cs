using ArgoBooks.Core.Models.Common;

namespace ArgoBooks.Core.Models.Assets;

/// <summary>Something the business keeps for years, whose cost is spread over the years it is used rather than counted as an expense the day it is bought.</summary>
public class FixedAsset : IRecord
{
    /// <summary>Unique identifier (AST-001).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public AssetType Type { get; set; } = AssetType.Equipment;

    [JsonPropertyName("purchaseDate")]
    public DateTime PurchaseDate { get; set; }

    /// <summary>What it cost before tax, in <see cref="Currency"/>.</summary>
    [JsonPropertyName("cost")]
    public decimal Cost { get; set; }

    /// <summary>The currency every amount on this asset is in: the expense's when it came from one, otherwise the company's.</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    /// <summary>The expense it was bought with, whose cost the reports move from expenses to this asset.</summary>
    [JsonPropertyName("expenseId")]
    public string? ExpenseId { get; set; }

    [JsonPropertyName("method")]
    public DepreciationMethod Method { get; set; } = DepreciationMethod.StraightLine;

    /// <summary>Years it is depreciated over, for the straight line method.</summary>
    [JsonPropertyName("usefulLifeYears")]
    public decimal? UsefulLifeYears { get; set; }

    /// <summary>The share of its remaining value written off each year, as a percentage, for the declining balance method.</summary>
    [JsonPropertyName("ratePercent")]
    public decimal? RatePercent { get; set; }

    /// <summary>CRA's Capital Cost Allowance class, such as "10", for a Canadian company's asset.</summary>
    [JsonPropertyName("ccaClass")]
    public string? CcaClass { get; set; }

    /// <summary>
    /// The share of the rate claimed in the year it was bought, as CRA allows for its purchase date. Set only when it follows CRA's yearly method.
    /// </summary>
    [JsonPropertyName("firstYearFactor")]
    public decimal? FirstYearFactor { get; set; }

    /// <summary>What it is expected to be worth when the business is done with it. It is never depreciated below this.</summary>
    [JsonPropertyName("endValue")]
    public decimal EndValue { get; set; }

    /// <summary>Depreciation claimed in earlier books, before <see cref="TrackedFrom"/>.</summary>
    [JsonPropertyName("priorDepreciation")]
    public decimal PriorDepreciation { get; set; }

    /// <summary>The first day these books depreciate it, set only for an asset bought before it was added.</summary>
    [JsonPropertyName("trackedFrom")]
    public DateTime? TrackedFrom { get; set; }

    [JsonPropertyName("disposedDate")]
    public DateTime? DisposedDate { get; set; }

    [JsonPropertyName("disposalKind")]
    public AssetDisposalKind? DisposalKind { get; set; }

    /// <summary>What it sold for, in <see cref="Currency"/>. Zero when it was scrapped or given away.</summary>
    [JsonPropertyName("salePrice")]
    public decimal SalePrice { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsDisposed => DisposedDate.HasValue;

    /// <summary>Whether it is depreciated by CRA's yearly method, a declining balance with its own first-year share, rather than month by month.</summary>
    [JsonIgnore]
    public bool FollowsCca => Method == DepreciationMethod.DecliningBalance && FirstYearFactor.HasValue && !string.IsNullOrEmpty(CcaClass);

    /// <summary>What it brought in when it left the books.</summary>
    [JsonIgnore]
    public decimal Proceeds => DisposalKind == AssetDisposalKind.Sold ? SalePrice : 0m;

    public FixedAsset Clone() => (FixedAsset)MemberwiseClone();

    /// <summary>Writes another copy's values over this one, except the expense link, which renaming the expense keeps up to date.</summary>
    public void CopyFrom(FixedAsset other)
    {
        Name = other.Name;
        Type = other.Type;
        PurchaseDate = other.PurchaseDate;
        Cost = other.Cost;
        Currency = other.Currency;
        Method = other.Method;
        UsefulLifeYears = other.UsefulLifeYears;
        RatePercent = other.RatePercent;
        CcaClass = other.CcaClass;
        FirstYearFactor = other.FirstYearFactor;
        EndValue = other.EndValue;
        PriorDepreciation = other.PriorDepreciation;
        TrackedFrom = other.TrackedFrom;
        DisposedDate = other.DisposedDate;
        DisposalKind = other.DisposalKind;
        SalePrice = other.SalePrice;
        Notes = other.Notes;
        UpdatedAt = other.UpdatedAt;
    }
}

public enum AssetType
{
    Land,
    Building,
    Vehicle,
    Equipment,
    FurnitureAndFixtures,
    ComputerEquipment,
    Other
}

public enum DepreciationMethod
{
    StraightLine,
    DecliningBalance
}

public enum AssetDisposalKind
{
    Sold,
    Scrapped
}

public static class AssetTypeExtensions
{
    public static string ToDisplayName(this AssetType type) => type switch
    {
        AssetType.FurnitureAndFixtures => "Furniture and fixtures",
        AssetType.ComputerEquipment => "Computer equipment",
        _ => type.ToString()
    };

    public static AssetType ParseAssetType(string displayName) =>
        Enum.GetValues<AssetType>().FirstOrDefault(t => t.ToDisplayName() == displayName, AssetType.Other);
}
