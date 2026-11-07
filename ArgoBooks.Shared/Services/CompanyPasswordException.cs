namespace ArgoBooks.Core.Services;

/// <summary>The password for an encrypted company file is missing or wrong, kept apart from the system refusing access to the file itself.</summary>
public sealed class CompanyPasswordException(string message) : UnauthorizedAccessException(message);
