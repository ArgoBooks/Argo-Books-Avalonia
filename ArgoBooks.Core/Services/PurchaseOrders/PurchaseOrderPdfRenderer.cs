using System.Globalization;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Models.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArgoBooks.Core.Services.PurchaseOrders;

/// <summary>
/// Renders a purchase order to a PDF byte array using QuestPDF.
/// </summary>
public static class PurchaseOrderPdfRenderer
{
    public static byte[] Render(PurchaseOrder order, CompanyData companyData, string currencySymbol = "$")
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var supplier = companyData.GetSupplier(order.SupplierId);
        var company = companyData.Settings.Company;
        var products = companyData.Products;

        using var ms = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Helvetica"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(company.Name)
                                .FontSize(18).SemiBold();
                            if (!string.IsNullOrWhiteSpace(company.Address))
                                left.Item().Text(company.Address);
                            var cityLine = string.Join(", ", new[] { company.City, company.ProvinceState, company.Country }
                                .Where(s => !string.IsNullOrWhiteSpace(s)));
                            if (!string.IsNullOrWhiteSpace(cityLine))
                                left.Item().Text(cityLine);
                            if (!string.IsNullOrWhiteSpace(company.Email))
                                left.Item().Text(company.Email);
                            if (!string.IsNullOrWhiteSpace(company.Phone))
                                left.Item().Text(company.Phone);
                        });

                        row.ConstantItem(180).AlignRight().Column(right =>
                        {
                            right.Item().AlignRight().Text("PURCHASE ORDER")
                                .FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                            right.Item().AlignRight().Text(order.PoNumber)
                                .FontSize(12).FontColor(Colors.Grey.Darken2);
                        });
                    });
                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(16);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(s =>
                        {
                            s.Item().Text("SUPPLIER").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                            s.Item().PaddingTop(4).Text(supplier?.Name ?? "(Unknown supplier)").SemiBold();
                            if (supplier != null)
                            {
                                if (!string.IsNullOrWhiteSpace(supplier.ContactPerson))
                                    s.Item().Text(supplier.ContactPerson);
                                if (!string.IsNullOrWhiteSpace(supplier.Address.Street))
                                    s.Item().Text(supplier.Address.Street);
                                var supCity = string.Join(", ", new[]
                                {
                                    supplier.Address.City,
                                    supplier.Address.State,
                                    supplier.Address.ZipCode
                                }.Where(x => !string.IsNullOrWhiteSpace(x)));
                                if (!string.IsNullOrWhiteSpace(supCity))
                                    s.Item().Text(supCity);
                                if (!string.IsNullOrWhiteSpace(supplier.Address.Country))
                                    s.Item().Text(supplier.Address.Country);
                                if (!string.IsNullOrWhiteSpace(supplier.Email))
                                    s.Item().Text(supplier.Email);
                            }
                        });

                        row.ConstantItem(200).Column(d =>
                        {
                            d.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Order Date:").FontColor(Colors.Grey.Darken2);
                                r.ConstantItem(100).AlignRight().Text(order.OrderDate.ToString("yyyy-MM-dd"));
                            });
                            d.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Expected:").FontColor(Colors.Grey.Darken2);
                                r.ConstantItem(100).AlignRight().Text(order.ExpectedDeliveryDate.ToString("yyyy-MM-dd"));
                            });
                            if (!string.IsNullOrWhiteSpace(supplier?.PaymentTerms))
                            {
                                d.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Payment Terms:").FontColor(Colors.Grey.Darken2);
                                    r.ConstantItem(100).AlignRight().Text(supplier.PaymentTerms);
                                });
                            }
                        });
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(4);
                            c.RelativeColumn(1);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text("Item").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Qty").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Unit Cost").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Total").SemiBold();
                        });

                        foreach (var li in order.LineItems)
                        {
                            var product = products.FirstOrDefault(p => p.Id == li.ProductId);
                            var name = product?.Name ?? "(Unknown product)";
                            var sku = product?.Sku;
                            var displayName = string.IsNullOrWhiteSpace(sku) ? name : $"{name}  ({sku})";

                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(displayName);
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight()
                                .Text(li.Quantity.ToString(CultureInfo.InvariantCulture));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight()
                                .Text($"{currencySymbol}{li.UnitCost:N2}");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight()
                                .Text($"{currencySymbol}{li.Total:N2}");
                        }
                    });

                    col.Item().AlignRight().Width(220).Column(t =>
                    {
                        t.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Subtotal").FontColor(Colors.Grey.Darken2);
                            r.ConstantItem(110).AlignRight().Text($"{currencySymbol}{order.Subtotal:N2}");
                        });
                        t.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Shipping").FontColor(Colors.Grey.Darken2);
                            r.ConstantItem(110).AlignRight().Text($"{currencySymbol}{order.ShippingCost:N2}");
                        });
                        t.Item().PaddingVertical(4).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                        t.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Total").SemiBold();
                            r.ConstantItem(110).AlignRight().Text($"{currencySymbol}{order.Total:N2}").SemiBold();
                        });
                    });

                    if (!string.IsNullOrWhiteSpace(order.Notes))
                    {
                        col.Item().PaddingTop(8).Column(n =>
                        {
                            n.Item().Text("Notes").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                            n.Item().PaddingTop(4).Text(order.Notes);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.Span(" of ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    t.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf(ms);

        return ms.ToArray();
    }
}
