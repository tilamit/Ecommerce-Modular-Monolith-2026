using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShopHub.Shared.Kernel.Exceptions;

namespace ShopHub.Modules.Catalog.Features.Products;

/// <summary>Where uploaded product images live and what is accepted (bound from configuration).</summary>
internal sealed class ProductImageOptions
{
    internal const string SectionName = "Catalog:ProductImages";

    /// <summary>Folder under the content root. Uploaded content, so it is not source controlled.</summary>
    public string FolderName { get; set; } = "ProductImages";

    public int MaxBytesPerFile { get; set; } = 5 * 1024 * 1024;

    public int MaxFilesPerRequest { get; set; } = 10;
}

/// <summary>One stored file, as the admin screen needs to reference it.</summary>
internal sealed record StoredImage(string FileName, string Url, long SizeBytes);

/// <summary>
/// Saves uploaded product images to disk and hands back the URL to record against the
/// product.
/// </summary>
/// <remarks>
/// <para>
/// The file name is generated here and never taken from the upload. A caller-supplied name
/// is the usual way a path separator or a <c>..</c> segment reaches
/// <see cref="Path.Combine(string, string)"/> and it is also how one upload silently
/// overwrites another. Reads are checked a second time against the same pattern, so a
/// crafted request cannot walk out of the folder even if a name is somehow forged.
/// </para>
/// <para>
/// Only the extension allow-list is trusted, not the browser's content type, which the
/// caller controls. This does not attempt to verify that the bytes really are an image: the
/// files are served back with a fixed content type from a route that never executes them,
/// which is the property that matters here.
/// </para>
/// <para>
/// Nothing deletes. Detaching an image from a product, or deleting the product, leaves the
/// file on disk, which is deliberate: product deletion is a soft delete (spec A7), so a row
/// can come back and would find its images gone. The same URL can also be attached to more
/// than one product. Reclaiming unreferenced files is a sweep over the folder against the
/// <c>ProductImages</c> table and is not built here.
/// </para>
/// </remarks>
internal sealed class ProductImageStorage
{
    /// <summary>Extension to the content type the file is served back as.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".avif"] = "image/avif",
    };

    /// <summary>Exactly what <see cref="SaveAsync"/> generates and nothing else.</summary>
    private static readonly Regex StoredNamePattern = new(
        @"^[0-9a-f]{32}\.(jpg|jpeg|png|webp|gif|avif)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly ProductImageOptions _options;

    public ProductImageStorage(IOptions<ProductImageOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        _options = options.Value;
        Root = Path.Combine(environment.ContentRootPath, _options.FolderName);
    }

    /// <summary>The folder on disk. Created on first write rather than at startup.</summary>
    internal string Root { get; }

    /// <summary>The route uploaded images are served from and what is stored in the database.</summary>
    internal static string UrlFor(string fileName) => $"/api/v1/catalog/products/images/{fileName}";

    /// <summary>
    /// Saves every file in one go, so the admin screen can attach a whole set at once.
    /// Rejects the batch rather than storing half of it.
    /// </summary>
    internal async Task<IReadOnlyList<StoredImage>> SaveAsync(
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            throw new DomainRuleException("no_files", "Select at least one image to upload.");
        }

        if (files.Count > _options.MaxFilesPerRequest)
        {
            throw new DomainRuleException(
                "too_many_files",
                $"Up to {_options.MaxFilesPerRequest} images can be uploaded at once.");
        }

        // Validated up front so a rejected fifth file does not leave four on disk.
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName);

            if (!AllowedTypes.ContainsKey(extension))
            {
                throw new DomainRuleException(
                    "unsupported_image_type",
                    $"'{file.FileName}' is not a supported image. Allowed: {string.Join(", ", AllowedTypes.Keys)}.");
            }

            if (file.Length <= 0)
            {
                throw new DomainRuleException("empty_file", $"'{file.FileName}' is empty.");
            }

            if (file.Length > _options.MaxBytesPerFile)
            {
                throw new DomainRuleException(
                    "image_too_large",
                    $"'{file.FileName}' exceeds the {_options.MaxBytesPerFile / (1024 * 1024)} MB limit.");
            }
        }

        Directory.CreateDirectory(Root);

        var stored = new List<StoredImage>(files.Count);

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = $"{Guid.CreateVersion7():N}{extension}";
            var path = Path.Combine(Root, fileName);

            await using (var target = File.Create(path))
            {
                await file.CopyToAsync(target, cancellationToken);
            }

            stored.Add(new StoredImage(fileName, UrlFor(fileName), file.Length));
        }

        return stored;
    }

    /// <summary>
    /// Resolves a stored file for reading, or null when the name is not one this class
    /// generated or the file is gone.
    /// </summary>
    internal (string Path, string ContentType, DateTimeOffset LastModified)? Resolve(string fileName)
    {
        if (!StoredNamePattern.IsMatch(fileName))
        {
            return null;
        }

        var path = Path.Combine(Root, fileName);

        // Belt and braces. The pattern already forbids separators, so this can only fail if
        // the pattern is ever loosened - which is exactly when it needs to be caught.
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(Root), StringComparison.Ordinal))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var contentType = AllowedTypes[Path.GetExtension(fileName)];

        return (path, contentType, File.GetLastWriteTimeUtc(path));
    }
}
