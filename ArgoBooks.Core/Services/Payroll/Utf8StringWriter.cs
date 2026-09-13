using System.Globalization;
using System.Text;

namespace ArgoBooks.Core.Services.Payroll;

/// <summary>
/// A StringWriter that reports UTF-8, because an XML declaration is taken from the writer.
///
/// Save(TextWriter) asks the writer what encoding it is, and a plain StringWriter answers UTF-16
/// whatever is done with the characters afterwards. The export then writes those characters out
/// as UTF-8 bytes, so the file announces an encoding it is not in. Nothing in this app notices;
/// the parser at the other end is what notices.
///
/// Shared rather than nested in one writer: the T4 export hit this, and the ROE export then hit
/// it again because the fix was not somewhere the next one would find it.
/// </summary>
internal sealed class Utf8StringWriter(StringBuilder builder)
    : StringWriter(builder, CultureInfo.InvariantCulture)
{
    public override Encoding Encoding => Encoding.UTF8;
}
