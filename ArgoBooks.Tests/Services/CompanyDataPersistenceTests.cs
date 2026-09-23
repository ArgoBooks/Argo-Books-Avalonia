using System.Collections;
using System.Reflection;
using ArgoBooks.Core.Data;
using ArgoBooks.Core.Services;
using Xunit;

namespace ArgoBooks.Tests.Services;

/// <summary>
/// Every collection on <see cref="CompanyData"/> survives a save and a reopen.
///
/// The general form of <see cref="PayrollPersistenceTests"/>. Saving is a hand-written list of
/// one write per collection, and a collection missing from that list is written nowhere and
/// reported nowhere: the app reads and writes it all session and drops it on close. That is how
/// employees and pay runs were lost. Reflecting over the collections rather than listing them
/// means a new one is covered the day it is added, not the day someone remembers this file.
/// </summary>
public class CompanyDataPersistenceTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(), $"argo-persist-{Guid.NewGuid():N}.argo");

    private readonly List<string> _temps = [];

    private static FileService Service() =>
        new(new CompressionService(), new FooterService(), new EncryptionService());

    private static List<PropertyInfo> Collections() =>
        typeof(CompanyData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            .OrderBy(p => p.Name)
            .ToList();

    /// <summary>
    /// One element of whatever the list holds, left at its defaults. The question here is
    /// whether the collection reaches the file at all, not whether a given field survives.
    /// </summary>
    private static object Element(Type elementType) =>
        elementType == typeof(string)
            ? "round-trip"
            : Activator.CreateInstance(elementType)
              ?? throw new InvalidOperationException(
                  $"{elementType.Name} cannot be constructed, so this test cannot cover it.");

    [Fact]
    public async Task EveryCollection_SurvivesASaveAndReopen()
    {
        FileService service = Service();
        await service.CreateCompanyAsync(_file, "Persistence Co");

        string temp = await Open(service);
        CompanyData data = await service.LoadCompanyDataAsync(temp);

        List<PropertyInfo> collections = Collections();
        Assert.NotEmpty(collections);

        foreach (PropertyInfo property in collections)
        {
            var list = (IList)property.GetValue(data)!;
            list.Add(Element(property.PropertyType.GetGenericArguments()[0]));
        }

        await service.SaveCompanyDataAsync(temp, data);
        await service.SaveCompanyAsync(_file, temp);

        // A second open, which is what closing and reopening the app does.
        FileService reopened = Service();
        CompanyData loaded = await reopened.LoadCompanyDataAsync(await Open(reopened));

        List<string> lost = collections
            .Where(p => ((IList)p.GetValue(loaded)!).Count == 0)
            .Select(p => p.Name)
            .ToList();

        Assert.True(
            lost.Count == 0,
            "Nothing writes these to the company file, so they are lost when it closes: "
            + string.Join(", ", lost)
            + ". Each needs a write in FileService.SaveCompanyDataAsync and a matching read in "
            + "LoadCompanyDataAsync.");
    }

    private async Task<string> Open(FileService service)
    {
        string temp = await service.OpenCompanyAsync(_file);
        _temps.Add(temp);
        return temp;
    }

    public void Dispose()
    {
        foreach (string temp in _temps)
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
            catch { /* A temp directory left behind is not worth failing a test run over. */ }
        }

        try { if (File.Exists(_file)) File.Delete(_file); }
        catch { /* As above. */ }

        GC.SuppressFinalize(this);
    }
}
